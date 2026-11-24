using System.Text;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PhotinoNetUtils.Hosting;

namespace ClipboardSync.Services;

/// <summary>
///     The data plane: the clipboard history and the device board are fetched from the local server
///     as binary <see cref="DataBlock" />s instead of being pushed through the message bridge as
///     JSON. The bridge still carries the commands, because they are requests and answers.
/// </summary>
internal static class DataRoutes
{
    public const ushort HistoryKind = 1;
    public const ushort DevicesKind = 2;

    /// <summary>
    ///     A block stores a cell's byte length in a u16, so a longer value cannot travel in one and
    ///     the block refuses to build. A single oversized clipboard text would therefore hide the
    ///     whole board, so a cell is clipped to this; the store keeps the full value and the copy
    ///     and apply paths read it from there.
    /// </summary>
    private const int MaxCellBytes = ushort.MaxValue;

    /// <summary>The history has no column for a row's position: the order is the order the store gives.</summary>
    public static void Map(WebApplication app, string token)
    {
        app.MapGet($"/data/{token}/history", (Delegate)(async (HttpContext _) =>
            DataBlockResults.Block(History(await SyncService.Instance.HistoryForDataAsync()))));

        app.MapGet($"/data/{token}/devices", (Delegate)(async (HttpContext _) =>
            DataBlockResults.Block(Devices(await SyncService.Instance.DevicesForDataAsync()))));
    }

    private static DataBlock History(HistoryPayload payload)
    {
        var block = new DataBlock(HistoryKind)
            .Text("id")
            .Text("originDeviceId")
            .Text("originDeviceName")
            .Text("kind")
            .Text("content")
            .Text("meta")
            .Text("direction")
            .Text("status")
            .Text("files")
            .Number("at")
            .Number("reached")
            .Number("peers")
            .Number("totalBytes")
            .Number("progress");

        // Rows are written in the order the store returns them, duplicates included: copying the same
        // text twice is two entries in the history, not one.
        foreach (var item in payload.Items)
            block.Row(
                [
                    item.Id,
                    item.OriginDeviceId,
                    Cell(item.OriginDeviceName),
                    item.Kind,
                    Cell(item.Content),
                    Cell(item.Meta ?? ""),
                    item.Direction,
                    item.Status,
                    Files(item.Files)
                ],
                item.At,
                item.Reached,
                item.Peers,
                item.TotalBytes,
                item.Progress);

        return block;
    }

    private static DataBlock Devices(DevicesPayload payload)
    {
        var block = new DataBlock(DevicesKind)
            .Text("scope")
            .Text("id")
            .Text("name")
            .Text("host")
            .Number("port")
            .Number("self")
            .Number("online")
            .Number("items");

        block.Row(["self", payload.Self.Id, Cell(payload.Self.Name), ""],
            0, payload.Self.Self ? 1 : 0, payload.Self.Online ? 1 : 0, payload.Self.ItemCount);

        foreach (var peer in payload.Peers)
            block.Row(["peer", peer.Id, Cell(peer.Name), ""],
                0, peer.Self ? 1 : 0, peer.Online ? 1 : 0, peer.ItemCount);

        foreach (var device in payload.Discovered)
            block.Row(["discovered", device.Id, Cell(device.Name), Cell(device.Host)], device.Port, 0, 0, 0);

        return block;
    }

    /// <summary>
    ///     The files of a file item as one string: path, size and directory flag, with unit and
    ///     record separators, which a path cannot contain. Only the complete entries that fit are
    ///     sent, so the interface never reads a half-written one.
    /// </summary>
    private static string Files(ClipboardFileEntry[]? files)
    {
        if (files is not { Length: > 0 })
            return "";

        var builder = new StringBuilder();
        var used = 0;

        foreach (var file in files)
        {
            var entry = $"{file.Path}\u001f{file.Size}\u001f{(file.Directory ? 1 : 0)}";
            var length = Encoding.UTF8.GetByteCount(entry) + (builder.Length > 0 ? 1 : 0);

            if (used + length > MaxCellBytes)
                break;

            if (builder.Length > 0)
                builder.Append('\u001e');

            builder.Append(entry);
            used += length;
        }

        return builder.ToString();
    }

    private static string Cell(string value) =>
        Encoding.UTF8.GetByteCount(value) <= MaxCellBytes ? value : Clip(value);

    /// <summary>Keeps the start of a value, stopping on a character boundary.</summary>
    private static string Clip(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var length = MaxCellBytes;

        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            length--;

        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
