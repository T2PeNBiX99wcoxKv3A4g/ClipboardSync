using System.Collections.Concurrent;

namespace ClipboardSync.Services;

/// <summary>
///     Tracks which paired devices answer right now. A check performs a real authenticated handshake,
///     so a stale address that now belongs to another device counts as offline.
/// </summary>
[Singleton]
internal partial class PresenceService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2.5);
    private readonly Lock _gate = new();

    private readonly ConcurrentDictionary<string, bool> _online = new(StringComparer.Ordinal);
    private CancellationTokenSource? _cancellation;
    private bool _checking;
    private string _published = "";

    public bool IsOnline(string deviceId) => _online.TryGetValue(deviceId, out var online) && online;

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            Stop();
            _online.Clear();
            Publish();
            return;
        }

        lock (_gate)
        {
            if (_cancellation is not null)
                return;

            _cancellation = new();
            _ = Task.Run(() => LoopAsync(_cancellation.Token));
        }
    }

    public void MarkOnline(string deviceId)
    {
        if (_online.TryGetValue(deviceId, out var online) && online)
            return;

        _online[deviceId] = true;
        Publish();
    }

    public void MarkOffline(string deviceId)
    {
        if (_online.TryGetValue(deviceId, out var online) && !online)
            return;

        _online[deviceId] = false;
        Publish();
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await CheckAsync(token);

            try
            {
                await Task.Delay(Interval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAsync(CancellationToken token)
    {
        lock (_gate)
        {
            if (_checking)
                return;

            _checking = true;
        }

        try
        {
            var peers = PeerStore.Instance.All;
            if (peers.Count == 0)
                return;

            var results = await Task.WhenAll(peers.Select(async peer =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(ProbeTimeout);
                return (peer.Id, Alive: await PeerClient.ProbeAsync(peer, cts.Token));
            }));

            foreach (var (id, alive) in results)
                _online[id] = alive;

            Publish();
        }
        catch (OperationCanceledException)
        {
            // Stopping or probing was cancelled.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to check which paired devices are reachable");
        }
        finally
        {
            lock (_gate)
                _checking = false;
        }
    }

    private void Publish()
    {
        var ids = _online.Where(entry => entry.Value).Select(entry => entry.Key).Order(StringComparer.Ordinal).ToArray();
        var signature = string.Join(',', ids);
        if (signature == _published)
            return;

        _published = signature;
        if (!SyncService.IsInitialized)
            return;

        SyncService.Instance.OnPresenceChanged();
    }
}