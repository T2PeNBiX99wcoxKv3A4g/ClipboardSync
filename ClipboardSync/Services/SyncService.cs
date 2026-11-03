using System.Security.Cryptography;
using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Platform;
using ClipboardSync.Security;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     The local half of the clipboard mesh: watches the clipboard, persists every item, sends new
///     items to paired devices over encrypted sessions, and accepts items from them. The board is
///     grouped by origin device, so every device can show the same list.
/// </summary>
internal sealed class SyncService : Singleton<SyncService>
{
    private readonly ClipboardService _clipboard = ClipboardService.Instance;
    private readonly ClipboardStore _store = ClipboardStore.Instance;
    private readonly DiscoveryService _discovery = DiscoveryService.Instance;
    private readonly DeviceIdentity _identity = DeviceIdentity.Instance;
    private readonly PeerStore _peers = PeerStore.Instance;
    private readonly PresenceService _presence = PresenceService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly PeerServer _server = PeerServer.Instance;
    private readonly SemaphoreSlim _receiveGate = new(1, 1);

    private string? _suppressText;
    private long _lastImageAt;
    private long _suppressAt;

    public SyncService()
    {
        NativeBridge.MessageReceived += HandleMessageAsync;
        _clipboard.TextCopied += OnTextCopied;
        _clipboard.ImageCopied += OnImageCopied;
        SettingsService.Changed += _ => ApplyCurrent();

        ApplyCurrent();
        _ = Task.Run(RetryLoopAsync);
    }

    /// <summary>Called by the listener when a session was authenticated.</summary>
    public async Task HandleSessionAsync(SecureSession session, CancellationToken token)
    {
        _presence.MarkOnline(session.PeerDeviceId);

        while (!token.IsCancellationRequested)
        {
            var payload = await session.ReceiveAsync(token);
            if (payload is null)
                break;

            switch (FrameProtocol.ReadType(payload))
            {
                case PeerProtocol.ClipboardItem:
                    var message = JsonSerializer.Deserialize(payload, AppJsonContext.Default.ClipboardItemMessage);
                    if (message is not null)
                        await ReceiveItemAsync(session, message, token);
                    break;
            }
        }
    }

    /// <summary>Called by the pairing flow after a peer is pinned; sends the board as a catch-up.</summary>
    public void OnPeerPaired(PairedDevice peer)
    {
        _presence.MarkOnline(peer.Id);

        // A re-pair must not trust delivery state from an earlier session with the same id; the peer
        // may have a fresh history. Clearing it makes the catch-up below send every record again.
        _store.ForgetPeer(peer.Id);
        PublishBoard();

        _ = Task.Run(async () =>
        {
            foreach (var record in _store.Snapshot().Take(_settings.Current.HistoryLimit))
            {
                _store.MarkPending(record.Id, peer.Id);
                await DeliverAsync(record, peer);
            }
        });
    }

    public void OnDiscoveryChanged() => PublishDevices();

    public void OnPresenceChanged() => PublishDevices();

    private void ApplyCurrent()
    {
        var settings = _settings.Current;
        var enabled = settings.SyncEnabled;

        ClipboardLimits.MaxImageBytes =
            Math.Clamp(settings.MaxImageMb, ClipboardLimits.MinImageMb, ClipboardLimits.MaxImageMb) * 1024 * 1024;

        if (enabled && (settings.SyncText || settings.SyncImages))
            _clipboard.Start();
        else
            _clipboard.Stop();

        _server.Apply(enabled);
        _discovery.Apply(enabled, settings.DeviceName);
        _presence.Apply(enabled);
        PublishBoard();

        if (enabled)
            _ = Task.Run(SyncMissingAsync);
    }

    private void OnTextCopied(string text)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncText)
            return;

        // Klipper may report a text representation for the same image copy; the image event wins.
        if (Environment.TickCount64 - _lastImageAt < 1_500)
            return;

        if (text.Length is 0 or > PeerProtocol.MaxClipboardText)
            return;

        if (_suppressText == text && Environment.TickCount64 - _suppressAt < 5_000)
        {
            _suppressText = null;
            return;
        }

        var latest = _store.Snapshot().FirstOrDefault(item => item.OriginDeviceId == _identity.Id);
        if (latest?.Content == text)
            return;

        var record = new ClipboardRecord
        {
            OriginDeviceId = _identity.Id,
            OriginDeviceName = _settings.Current.DeviceName,
            Kind = "text",
            Content = text,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (!_store.Add(record))
            return;

        Log.Information("Captured a clipboard item ({Length} chars)", text.Length);
        PublishBoard();
        _ = BroadcastAsync(record);
    }

    private void OnImageCopied(byte[] png)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncImages)
            return;

        if (png.Length == 0 || png.Length > ClipboardLimits.MaxImageBytes)
            return;

        _lastImageAt = Environment.TickCount64;
        var hash = Convert.ToHexString(SHA256.HashData(png));

        var latest = _store.Snapshot()
            .FirstOrDefault(item => item.OriginDeviceId == _identity.Id && item.Kind == "image");
        if (latest?.Hash == hash)
            return;

        var id = Guid.NewGuid().ToString("N");
        string dataFile;

        try
        {
            dataFile = ClipboardStore.SaveBinary(id, png, ".png");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to store a copied image");
            return;
        }

        var record = new ClipboardRecord
        {
            Id = id,
            OriginDeviceId = _identity.Id,
            OriginDeviceName = _settings.Current.DeviceName,
            Kind = "image",
            Content = "Image",
            Meta = $"PNG · {FormatSize(png.Length)}",
            DataFile = dataFile,
            Hash = hash,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (!_store.Add(record))
            return;

        Log.Information("Captured a clipboard image ({Size})", FormatSize(png.Length));
        PublishBoard();
        _ = BroadcastAsync(record);
    }

    private async Task BroadcastAsync(ClipboardRecord record)
    {
        var peers = _peers.All;
        foreach (var peer in peers)
            _store.MarkPending(record.Id, peer.Id);

        PublishHistory();
        await Task.WhenAll(peers.Select(peer => DeliverAsync(record, peer)));
    }

    private async Task DeliverAsync(ClipboardRecord record, PairedDevice peer)
    {
        if (record.OriginDeviceId == peer.Id || record.DeliveredTo.Contains(peer.Id))
            return;

        try
        {
            using var cts = new CancellationTokenSource(PeerProtocol.ConnectionTimeout);
            using var session = await PeerClient.ConnectAsync(peer, cts.Token);
            _presence.MarkOnline(peer.Id);

            string? data = null;
            if (record.Kind == "image")
            {
                var bytes = ClipboardStore.ReadBinary(record.DataFile);
                if (bytes is null)
                {
                    _store.MarkFailed(record.Id, peer.Id);
                    return;
                }

                data = Convert.ToBase64String(bytes);
            }

            var message = new ClipboardItemMessage(PeerProtocol.ClipboardItem, record.Id, record.OriginDeviceId,
                record.OriginDeviceName, record.Kind, record.Content, record.Meta, record.At, data);

            await session.SendJsonAsync(message, AppJsonContext.Default.ClipboardItemMessage, cts.Token);
            var ack = await session.ReceiveJsonAsync(AppJsonContext.Default.ClipboardAckMessage, cts.Token);

            if (ack is { Ok: true })
                _store.MarkDelivered(record.Id, peer.Id);
            else
                _store.MarkFailed(record.Id, peer.Id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not deliver an item to {Device}", peer.Name);
            _presence.MarkOffline(peer.Id);
            _store.MarkFailed(record.Id, peer.Id);
        }
        finally
        {
            PublishHistory();
            PublishDevices();
        }
    }

    private async Task ReceiveItemAsync(SecureSession session, ClipboardItemMessage message, CancellationToken token)
    {
        // Two peers can deliver the same item at the same time (a catch-up and a live send). Storing
        // images writes a file, so serialise the whole receive path and check the id under the gate.
        await _receiveGate.WaitAsync(token);
        try
        {
            await ReceiveItemUnlockedAsync(session, message, token);
        }
        finally
        {
            _receiveGate.Release();
        }
    }

    private async Task ReceiveItemUnlockedAsync(SecureSession session, ClipboardItemMessage message,
        CancellationToken token)
    {
        if (!_settings.Current.SyncEnabled || message.Id.Length == 0)
        {
            await AcknowledgeAsync(session, message.Id, false, "Syncing is disabled on this device.", token);
            return;
        }

        var peer = _peers.Find(session.PeerDeviceId);
        var originId = message.OriginDeviceId.Length > 0 ? message.OriginDeviceId : session.PeerDeviceId;
        var originName = message.OriginDeviceName.Length > 0
            ? message.OriginDeviceName
            : peer?.Name ?? session.PeerDeviceId;
        var at = message.At > 0 ? message.At : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        ClipboardRecord record;

        if (message.Kind == "text")
        {
            if (!_settings.Current.SyncText || message.Content.Length is 0 or > PeerProtocol.MaxClipboardText)
            {
                await AcknowledgeAsync(session, message.Id, false, "Unsupported clipboard text.", token);
                return;
            }

            record = new ClipboardRecord
            {
                Id = message.Id,
                OriginDeviceId = originId,
                OriginDeviceName = originName,
                Kind = "text",
                Content = message.Content,
                Meta = message.Meta,
                At = at
            };
        }
        else if (message.Kind == "image")
        {
            if (!_settings.Current.SyncImages || message.Data is not { Length: > 0 })
            {
                await AcknowledgeAsync(session, message.Id, false, "Unsupported clipboard image.", token);
                return;
            }

            byte[] png;
            try
            {
                png = Convert.FromBase64String(message.Data);
            }
            catch (FormatException)
            {
                await AcknowledgeAsync(session, message.Id, false, "Malformed image payload.", token);
                return;
            }

            if (png.Length == 0 || png.Length > ClipboardLimits.MaxImageBytes)
            {
                await AcknowledgeAsync(session, message.Id, false, "The image is too large.", token);
                return;
            }

            string dataFile;
            try
            {
                if (_store.Find(message.Id) is not null)
                {
                    await AcknowledgeAsync(session, message.Id, true, null, token);
                    return;
                }

                dataFile = ClipboardStore.SaveBinary(message.Id, png, ".png");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to store a received image");
                await AcknowledgeAsync(session, message.Id, false, "The image could not be stored.", token);
                return;
            }

            record = new ClipboardRecord
            {
                Id = message.Id,
                OriginDeviceId = originId,
                OriginDeviceName = originName,
                Kind = "image",
                Content = message.Content.Length > 0 ? message.Content : "Image",
                Meta = message.Meta ?? $"PNG · {FormatSize(png.Length)}",
                DataFile = dataFile,
                Hash = Convert.ToHexString(SHA256.HashData(png)),
                At = at
            };
        }
        else
        {
            await AcknowledgeAsync(session, message.Id, false, "Unsupported clipboard item.", token);
            return;
        }

        if (_store.Add(record))
        {
            Log.Information("Received a clipboard item from {Device}", record.OriginDeviceName);

            if (record.Kind == "image")
            {
                var png = ClipboardStore.ReadBinary(record.DataFile);
                if (png is not null)
                    ApplyRemoteImage(png);
            }
            else
            {
                ApplyRemoteText(record.Content);
            }

            PublishBoard();
            _ = RelayAsync(record, session.PeerDeviceId);
        }

        await AcknowledgeAsync(session, message.Id, true, null, token);
    }

    private static Task AcknowledgeAsync(SecureSession session, string id, bool ok, string? error,
        CancellationToken token) =>
        session.SendJsonAsync(new ClipboardAckMessage(PeerProtocol.ClipboardAck, id, ok, error),
            AppJsonContext.Default.ClipboardAckMessage, token);

    private void ApplyRemoteText(string text)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncText)
            return;

        _suppressText = text;
        _suppressAt = Environment.TickCount64;

        if (!_clipboard.WriteText(text))
            _suppressText = null;
    }

    private void ApplyRemoteImage(byte[] png)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncImages)
            return;

        _lastImageAt = Environment.TickCount64;
        _clipboard.WriteImage(png);
    }

    private static string FormatSize(int bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{Math.Max(1, bytes / 1024d):0} KB";

    private async Task RetryLoopAsync()
    {
        while (true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
            }
            catch (Exception)
            {
                return;
            }

            if (_settings.Current.SyncEnabled)
                await SyncMissingAsync();
        }
    }

    /// <summary>
    ///     Makes every device's board converge: for each item, any peer that has not acknowledged it
    ///     is sent a copy. This covers items copied before a device was paired, items that failed while
    ///     a peer was offline, and items relayed through another device.
    /// </summary>
    private async Task SyncMissingAsync()
    {
        var peers = _peers.All;
        if (peers.Count == 0)
            return;

        foreach (var record in _store.Snapshot().Take(200))
        {
            var targets = peers
                .Where(peer => peer.Id != record.OriginDeviceId && !record.DeliveredTo.Contains(peer.Id))
                .ToArray();

            if (targets.Length == 0)
                continue;

            foreach (var peer in targets)
                _store.MarkPending(record.Id, peer.Id);

            await Task.WhenAll(targets.Select(peer => DeliverAsync(record, peer)));
        }
    }

    private async Task RelayAsync(ClipboardRecord record, string senderDeviceId)
    {
        var peers = _peers.All
            .Where(peer => peer.Id != senderDeviceId && peer.Id != record.OriginDeviceId)
            .ToArray();

        if (peers.Length == 0)
            return;

        foreach (var peer in peers)
            _store.MarkPending(record.Id, peer.Id);

        await Task.WhenAll(peers.Select(peer => DeliverAsync(record, peer)));
    }

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "devices.get" => Task.FromResult<NativeResponse?>(context.Reply("devices.get.result", BuildDevices())),
        "history.get" => Task.FromResult<NativeResponse?>(context.Reply("history.get.result", BuildHistory())),
        "image.get" => Task.FromResult<NativeResponse?>(GetImage(context)),
        "image.copy" => Task.FromResult<NativeResponse?>(CopyImage(context)),
        "device.remove" => Task.FromResult<NativeResponse?>(RemoveDevice(context)),
        "discovery.start" => StartDiscovery(context, true),
        "discovery.stop" => StartDiscovery(context, false),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    private static Task<NativeResponse?> StartDiscovery(NativeContext context, bool start)
    {
        if (start)
            DiscoveryService.Instance.BeginSearch();
        else
            DiscoveryService.Instance.EndSearch();

        return Task.FromResult<NativeResponse?>(context.Reply("discovery.result", new OkPayload()));
    }

    private NativeResponse GetImage(NativeContext context)
    {
        var record = FindImage(context);
        var bytes = record is null ? null : ClipboardStore.ReadBinary(record.DataFile);

        if (bytes is null)
            return context.Fail("image.get.result", "The image is not available.");

        return context.Reply("image.get.result",
            new ImagePayload($"data:image/png;base64,{Convert.ToBase64String(bytes)}"));
    }

    private NativeResponse CopyImage(NativeContext context)
    {
        var record = FindImage(context);
        var bytes = record is null ? null : ClipboardStore.ReadBinary(record.DataFile);

        if (bytes is null)
            return context.Fail("image.copy.result", "The image is not available.");

        return _clipboard.WriteImage(bytes)
            ? context.Reply("image.copy.result", new OkPayload())
            : context.Fail("image.copy.result", "The clipboard is busy.");
    }

    private ClipboardRecord? FindImage(NativeContext context)
    {
        var id = context.Payload<ImageRequest>()?.ItemId;
        if (id is not { Length: > 0 })
            return null;

        return _store.Find(id) is { Kind: "image" } record ? record : null;
    }

    private NativeResponse RemoveDevice(NativeContext context)
    {
        var id = context.Payload<DeviceRemoveRequest>()?.DeviceId;
        if (id is { Length: > 0 })
        {
            _peers.Remove(id);
            _store.ForgetPeer(id);
            PublishBoard();
        }

        return context.Reply("device.remove.result", new OkPayload());
    }

    private void PublishBoard()
    {
        PublishHistory();
        PublishDevices();
    }

    private void PublishDevices() =>
        WindowManager.Instance.NotifyMainWindow("devices.changed", BuildDevices());

    private void PublishHistory() =>
        WindowManager.Instance.NotifyMainWindow("history.changed", BuildHistory());

    private DevicesPayload BuildDevices()
    {
        var items = _store.Snapshot();
        var self = new DevicePayload(_identity.Id, _settings.Current.DeviceName, true, true,
            items.Count(item => item.OriginDeviceId == _identity.Id));

        var peers = _peers.All
            .OrderBy(peer => peer.Name, StringComparer.OrdinalIgnoreCase)
            .Select(peer => new DevicePayload(peer.Id, peer.Name, false, _presence.IsOnline(peer.Id),
                items.Count(item => item.OriginDeviceId == peer.Id)))
            .ToArray();

        var discovered = _discovery.Snapshot()
            .Where(device => _peers.Find(device.Id) is null)
            .Select(device => new DiscoveredDevicePayload(device.Id, device.Name, device.Host, device.Port))
            .ToArray();

        return new DevicesPayload(self, peers, discovered);
    }

    private HistoryPayload BuildHistory()
    {
        var peers = _peers.All.Count;
        var items = _store.Snapshot().Select(record => ToPayload(record, peers)).ToArray();
        return new HistoryPayload(items);
    }

    private ClipboardItemPayload ToPayload(ClipboardRecord record, int peers)
    {
        var local = record.OriginDeviceId == _identity.Id;
        var reached = local ? record.DeliveredTo.Count : 0;

        string status;
        if (!local)
        {
            status = "received";
        }
        else if (record.FailedFor.Count > 0)
        {
            status = "failed";
        }
        else if (record.PendingFor.Count > 0)
        {
            status = reached > 0 ? "transferring" : "waiting";
        }
        else if (peers == 0 || reached >= peers)
        {
            status = "synced";
        }
        else
        {
            status = "waiting";
        }

        return new ClipboardItemPayload(record.Id, record.OriginDeviceId, record.OriginDeviceName, record.Kind,
            record.Content, record.Meta, record.At, local ? "out" : "in", status, reached, peers);
    }
}
