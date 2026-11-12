using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;

namespace ClipboardSync.Services;

/// <summary>
///     The paired devices, kept in their own file instead of the UI settings. A device is added only
///     after its pairing handshake completes, and its pinned public key is what every later session
///     is checked against. Reads use the in-memory list; writes serialize and persist asynchronously.
/// </summary>
internal sealed class PeerStore : Singleton<PeerStore>
{
    private static readonly string FilePath = Path.Combine(Constants.SettingsDirectory, "peers.json");

    private readonly Lock _gate = new();
    private readonly List<PairedDevice> _peers = [];
    private readonly SemaphoreSlim _writeGate = new(1, 1);

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

    /// <summary>Reads the stored peers. The entry point waits for this once.</summary>
    public async Task InitializeAsync()
    {
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            var stored = JsonSerializer.Deserialize(json, AppJsonContext.Default.PairedDeviceArray);

            if (stored is null)
                return;

            lock (_gate)
            {
                _peers.Clear();
                _peers.AddRange(stored.Where(peer => peer.Id.Length > 0 && peer.PublicKey.Length > 0));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read {File}", FilePath);
        }
    }

    /// <summary>Adds a newly paired device, or refreshes the address and key of a known one.</summary>
    public async Task AddOrUpdateAsync(PairedDevice peer)
    {
        await _writeGate.WaitAsync();
        try
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
            }

            await SaveAsync();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string id)
    {
        await _writeGate.WaitAsync();
        try
        {
            bool removed;
            lock (_gate)
                removed = _peers.RemoveAll(peer => string.Equals(peer.Id, id, StringComparison.Ordinal)) > 0;

            if (removed)
                await SaveAsync();

            return removed;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SaveAsync()
    {
        PairedDevice[] snapshot;
        lock (_gate)
            snapshot = [.. _peers];

        try
        {
            await File.WriteAllTextAsync(FilePath,
                JsonSerializer.Serialize(snapshot, AppJsonContext.Default.PairedDeviceArray));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write {File}", FilePath);
        }
    }
}