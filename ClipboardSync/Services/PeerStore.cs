using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     The paired devices, kept in their own file instead of the UI settings. A device is added only
///     after its pairing handshake completes, and its pinned public key is what every later session
///     is checked against.
/// </summary>
internal sealed class PeerStore : Singleton<PeerStore>
{
    private static readonly string FilePath = Path.Combine(Constants.SettingsDirectory, "peers.json");

    private readonly Lock _gate = new();
    private readonly List<PairedDevice> _peers = [];

    public PeerStore() => Load();

    public IReadOnlyList<PairedDevice> All
    {
        get
        {
            lock (_gate)
                return [.. _peers];
        }
    }

    public PairedDevice? Find(string id)
    {
        lock (_gate)
            return _peers.FirstOrDefault(peer => string.Equals(peer.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Adds a newly paired device, or refreshes the address and key of a known one.</summary>
    public void AddOrUpdate(PairedDevice peer)
    {
        lock (_gate)
        {
            var existing = _peers.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, peer.Id, StringComparison.Ordinal));

            if (existing is null)
                _peers.Add(peer);
            else
            {
                existing.Name = peer.Name;
                existing.Host = peer.Host;
                existing.Port = peer.Port;
                existing.PublicKey = peer.PublicKey;
                existing.Addresses = peer.Addresses;
            }

            Save();
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var removed = _peers.RemoveAll(peer => string.Equals(peer.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
                Save();

            return removed;
        }
    }

    private void Load()
    {
        if (!File.Exists(FilePath))
            return;

        var stored = Try.Run(() => JsonSerializer.Deserialize(File.ReadAllText(FilePath),
                AppJsonContext.Default.PairedDeviceArray))
            .OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath))
            .GetOrNull();

        if (stored is null)
            return;

        lock (_gate)
        {
            _peers.Clear();
            _peers.AddRange(stored.Where(peer => peer.Id.Length > 0 && peer.PublicKey.Length > 0));
        }
    }

    /// <summary>Callers hold <see cref="_gate" />.</summary>
    private void Save() =>
        Try.Run(() => File.WriteAllText(FilePath,
                JsonSerializer.Serialize(_peers.ToArray(), AppJsonContext.Default.PairedDeviceArray)))
            .OnFailure(ex => Log.Warning(ex, "Failed to write {File}", FilePath));
}