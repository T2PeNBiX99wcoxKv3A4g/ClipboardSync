using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ClipboardSync.Security;

/// <summary>Length-prefixed JSON frames over any stream, used by both handshakes and sessions.</summary>
internal static class FrameProtocol
{
    /// <summary>Reads one frame, or null when the peer closed before sending a complete frame.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        try
        {
            await stream.ReadExactlyAsync(header, token);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > PeerProtocol.MaxFrame)
            return null;

        var frame = new byte[(int)length];
        try
        {
            await stream.ReadExactlyAsync(frame, token);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        return frame;
    }

    public static async Task WriteFrameAsync(Stream stream, byte[] frame, CancellationToken token)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)frame.Length);

        await stream.WriteAsync(header, token);
        await stream.WriteAsync(frame, token);
        await stream.FlushAsync(token);
    }

    /// <summary>Reads the <c>type</c> discriminator without committing to a message shape.</summary>
    public static string? ReadType(byte[] frame)
    {
        try
        {
            using var document = JsonDocument.Parse(frame);

            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase))
                    return property.Value.GetString();

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task WriteAsync<T>(Stream stream, T message, JsonTypeInfo<T> typeInfo,
        CancellationToken token)
    {
        var frame = JsonSerializer.SerializeToUtf8Bytes(message, typeInfo);
        await WriteFrameAsync(stream, frame, token);
    }

    public static T? Read<T>(byte[]? frame, JsonTypeInfo<T> typeInfo) where T : class =>
        frame is null ? null : JsonSerializer.Deserialize(frame, typeInfo);
}
