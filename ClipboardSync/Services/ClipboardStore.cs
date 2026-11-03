using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     The local copy of the replicated clipboard board. Every item, whoever copied it, lives here;
///     the UI groups by origin device. The file is intentionally in the local data folder: history can
///     grow and should not roam with the settings.
/// </summary>
internal sealed class ClipboardStore : Singleton<ClipboardStore>
{
    private static readonly string FilePath = Path.Combine(Constants.DataDirectory, "history.json");

    private readonly Lock _gate = new();
    private readonly List<ClipboardRecord> _items = [];

    public ClipboardStore() => Load();

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ClipboardRecord> Snapshot()
    {
        lock (_gate)
            return [.. _items.OrderByDescending(item => item.At)];
    }

    public ClipboardRecord? Find(string id)
    {
        lock (_gate)
            return _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Adds an item once. Returns false when the id is already known.</summary>
    public bool Add(ClipboardRecord record)
    {
        lock (_gate)
        {
            if (_items.Any(item => string.Equals(item.Id, record.Id, StringComparison.Ordinal)))
                return false;

            _items.Add(record);
            Trim();
            Save();
            return true;
        }
    }

    public void MarkPending(string id, string peerId)
    {
        lock (_gate)
        {
            var record = FindUnlocked(id);
            if (record is null) return;

            record.FailedFor.RemoveAll(peer => peer == peerId);
            if (!record.DeliveredTo.Contains(peerId) && !record.PendingFor.Contains(peerId))
                record.PendingFor.Add(peerId);

            Save();
        }
    }

    public void MarkDelivered(string id, string peerId)
    {
        lock (_gate)
        {
            var record = FindUnlocked(id);
            if (record is null) return;

            record.PendingFor.RemoveAll(peer => peer == peerId);
            record.FailedFor.RemoveAll(peer => peer == peerId);
            if (!record.DeliveredTo.Contains(peerId))
                record.DeliveredTo.Add(peerId);

            Save();
        }
    }

    public void MarkFailed(string id, string peerId)
    {
        lock (_gate)
        {
            var record = FindUnlocked(id);
            if (record is null) return;

            record.PendingFor.RemoveAll(peer => peer == peerId);
            if (!record.FailedFor.Contains(peerId))
                record.FailedFor.Add(peerId);

            Save();
        }
    }

    /// <summary>Forgets a device's delivery state when it is unpaired.</summary>
    public void ForgetPeer(string peerId)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var record in _items)
            {
                changed |= record.PendingFor.RemoveAll(peer => peer == peerId) > 0;
                changed |= record.DeliveredTo.RemoveAll(peer => peer == peerId) > 0;
                changed |= record.FailedFor.RemoveAll(peer => peer == peerId) > 0;
            }

            if (changed)
                Save();
        }
    }

    private ClipboardRecord? FindUnlocked(string id) =>
        _items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    /// <summary>Writes an image payload under the data folder and returns its relative path.</summary>
    public static string SaveBinary(string id, byte[] data, string extension)
    {
        var directory = Path.Combine(Constants.DataDirectory, "clipboard");
        Directory.CreateDirectory(directory);

        var name = id + extension;
        File.WriteAllBytes(Path.Combine(directory, name), data);
        return Path.Combine("clipboard", name);
    }

    public static byte[]? ReadBinary(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;

        var path = Path.Combine(Constants.DataDirectory, relativePath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static void DeleteBinary(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return;

        Try.Run(() =>
        {
            var path = Path.Combine(Constants.DataDirectory, relativePath);
            if (File.Exists(path))
                File.Delete(path);
        }).OnFailure(ex => Constants.Logger.Debug(ex, "Failed to delete {Path}", relativePath));
    }

    private void Trim()
    {
        var limit = Math.Clamp(SettingsService.Instance.Current.HistoryLimit, 10, 5000);
        if (_items.Count <= limit)
            return;

        foreach (var old in _items.OrderByDescending(item => item.At).Skip(limit).ToArray())
        {
            DeleteBinary(old.DataFile);
            _items.Remove(old);
        }
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
            return;

        var stored = Try.Run(() => JsonSerializer.Deserialize(File.ReadAllText(FilePath),
                AppJsonContext.Default.ClipboardRecordArray))
            .OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath))
            .GetOrNull();

        if (stored is null)
            return;

        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(stored.Where(item => item.Id.Length > 0 && item.Content.Length > 0));
            Trim();
        }
    }

    /// <summary>Callers hold <see cref="_gate" />.</summary>
    private void Save() =>
        Try.Run(() => File.WriteAllText(FilePath,
                JsonSerializer.Serialize(_items.ToArray(), AppJsonContext.Default.ClipboardRecordArray)))
            .OnFailure(ex => Log.Warning(ex, "Failed to write {File}", FilePath));
}
