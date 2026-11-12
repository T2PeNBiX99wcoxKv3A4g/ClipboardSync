using System.Security.Cryptography;
using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Platform;
using ClipboardSync.Security;
using PhotinoNetUtils.Messaging;

namespace ClipboardSync.Services;

/// <summary>
///     The local half of the clipboard mesh: watches the clipboard, persists every item, sends new
///     items to paired devices over encrypted sessions, and accepts items from them. The board is
///     grouped by origin device, so every device can show the same list.
/// </summary>
[Singleton]
internal partial class SyncService
{
    private readonly ClipboardService _clipboard = ClipboardService.Instance;
    private readonly DiscoveryService _discovery = DiscoveryService.Instance;
    private readonly DeviceIdentity _identity = DeviceIdentity.Instance;
    private readonly PeerStore _peers = PeerStore.Instance;
    private readonly PresenceService _presence = PresenceService.Instance;
    private readonly SemaphoreSlim _receiveGate = new(1, 1);
    private readonly PeerServer _server = PeerServer.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ClipboardStore _store = ClipboardStore.Instance;
    private long _lastClearBroadcast;
    private long _lastFileAt;

    private string? _lastFileSignature;
    private long _lastImageAt;
    private long _lastProgressPublish;
    private long _suppressAt;
    private string? _suppressText;

    private SyncService()
    {
        NativeBridge.MessageReceived += HandleMessageAsync;
        _clipboard.TextCopied += OnTextCopied;
        _clipboard.ImageCopied += OnImageCopied;
        _clipboard.FilesCopied += OnFilesCopied;
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

                case PeerProtocol.HistoryClear:
                    var clear = JsonSerializer.Deserialize(payload, AppJsonContext.Default.HistoryClearMessage);
                    if (clear is not null && await _store.ClearIfNewerAsync(clear.At))
                        PublishBoard();
                    break;
            }
        }
    }

    /// <summary>Called by the pairing flow after a peer is pinned; sends the board as a catch-up.</summary>
    public void OnPeerPaired(PairedDevice peer) => _ = Task.Run(() => OnPeerPairedAsync(peer));

    private async Task OnPeerPairedAsync(PairedDevice peer)
    {
        _presence.MarkOnline(peer.Id);

        // A re-pair must not trust delivery state from an earlier session with the same id; the peer
        // may have a fresh history. Clearing it makes the catch-up below send every record again.
        await _store.ForgetPeerAsync(peer.Id);
        PublishBoard();

        foreach (var record in (await _store.SnapshotAsync()).Take(_settings.Current.HistoryLimit))
        {
            await _store.MarkPendingAsync(record.Id, peer.Id);
            await DeliverAsync(record, peer);
        }
    }

    public void OnDiscoveryChanged() => PublishDevices();

    public void OnPresenceChanged() => PublishDevices();

    private void ApplyCurrent()
    {
        var settings = _settings.Current;
        var enabled = settings.SyncEnabled;

        ClipboardLimits.MaxImageBytes =
            Math.Clamp(settings.MaxImageMb, ClipboardLimits.MinImageMb, ClipboardLimits.MaxImageMb) * 1024 * 1024;
        ClipboardLimits.MaxFileBytes =
            Math.Clamp(settings.MaxFileMb, ClipboardLimits.MinFileMb, ClipboardLimits.MaxFileMb) * 1024L * 1024L;

        if (enabled && (settings.SyncText || settings.SyncImages || settings.SyncFiles))
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

        // Klipper may report a text representation for the same image or file copy; that event wins.
        if (Environment.TickCount64 - _lastImageAt < 1_500 ||
            Environment.TickCount64 - _lastFileAt < 1_500 ||
            text.Contains("file://", StringComparison.OrdinalIgnoreCase))
            return;

        if (text.Length is 0 or > PeerProtocol.MaxClipboardText)
            return;

        if (_suppressText == text && Environment.TickCount64 - _suppressAt < 5_000)
        {
            _suppressText = null;
            return;
        }

        _ = Task.Run(() => CaptureTextAsync(text));
    }

    private async Task CaptureTextAsync(string text)
    {
        var latest = (await _store.SnapshotAsync()).FirstOrDefault(item => item.OriginDeviceId == _identity.Id);
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

        if (!await _store.AddAsync(record))
            return;

        Log.Information("Captured a clipboard item ({Length} chars)", text.Length);
        PublishBoard();
        await BroadcastAsync(record);
    }

    private void OnImageCopied(byte[] png)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncImages)
            return;

        if (png.Length == 0 || png.Length > ClipboardLimits.MaxImageBytes)
            return;

        _lastImageAt = Environment.TickCount64;
        _ = Task.Run(() => CaptureImageAsync(png));
    }

    private async Task CaptureImageAsync(byte[] png)
    {
        var hash = Convert.ToHexString(SHA256.HashData(png));

        var latest = (await _store.SnapshotAsync())
            .FirstOrDefault(item => item.OriginDeviceId == _identity.Id && item.Kind == "image");
        if (latest?.Hash == hash)
            return;

        var id = Guid.NewGuid().ToString("N");
        string dataFile;

        try
        {
            dataFile = await ClipboardStore.SaveBinaryAsync(id, png, ".png");
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

        if (!await _store.AddAsync(record))
            return;

        Log.Information("Captured a clipboard image ({Size})", FormatSize(png.Length));
        PublishBoard();
        await BroadcastAsync(record);
    }

    private void OnFilesCopied(string[] paths)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncFiles || paths.Length == 0)
            return;

        _lastFileAt = Environment.TickCount64;
        _ = Task.Run(() => CaptureFilesAsync(paths));
    }

    private async Task CaptureFilesAsync(string[] paths)
    {
        var id = Guid.NewGuid().ToString("N");
        ClipboardFileSnapshot? snapshot;

        try
        {
            snapshot = await ClipboardFiles.SnapshotAsync(paths, ClipboardLimits.MaxFileBytes);
        }
        catch (ClipboardFiles.FileLimitException)
        {
            Log.Warning("A copied file selection is larger than {Limit} or has too many entries",
                ClipboardFiles.FormatSize(ClipboardLimits.MaxFileBytes));
            await _store.CollectGarbageAsync();
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to snapshot a copied file selection");
            return;
        }

        if (snapshot is null)
            return;

        if (snapshot.Signature == _lastFileSignature)
            return;

        _lastFileSignature = snapshot.Signature;
        var count = snapshot.Files.Count(entry => !entry.Directory);
        var content = snapshot.Roots.Length == 1
            ? Path.GetFileName(snapshot.Roots[0])
            : $"{snapshot.Roots.Length} items";

        var record = new ClipboardRecord
        {
            Id = id,
            OriginDeviceId = _identity.Id,
            OriginDeviceName = _settings.Current.DeviceName,
            Kind = "file",
            Content = content,
            Meta = count == 0
                ? "Empty folder"
                : $"{count} files · {ClipboardFiles.FormatSize(snapshot.TotalBytes)}",
            Files = [.. snapshot.Files],
            Roots = [.. snapshot.Roots],
            TotalBytes = snapshot.TotalBytes,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (!await _store.AddAsync(record))
            return;

        Log.Information("Captured a clipboard file selection ({Count} files, {Size})", count,
            ClipboardFiles.FormatSize(snapshot.TotalBytes));
        PublishBoard();
        _ = BroadcastAsync(record);
    }

    private async Task BroadcastAsync(ClipboardRecord record)
    {
        var peers = _peers.All;
        foreach (var peer in peers)
            await _store.MarkPendingAsync(record.Id, peer.Id);

        PublishHistory();
        await Task.WhenAll(peers.Select(peer => DeliverAsync(record, peer)));
    }

    private async Task DeliverAsync(ClipboardRecord record, PairedDevice peer)
    {
        if (record.OriginDeviceId == peer.Id || record.DeliveredTo.Contains(peer.Id))
            return;

        try
        {
            using var cts = new CancellationTokenSource(PeerProtocol.TransferTimeout);
            using var session = await PeerClient.ConnectAsync(peer, cts.Token);
            _presence.MarkOnline(peer.Id);

            string? data = null;
            if (record.Kind == "image")
            {
                var bytes = await ClipboardStore.ReadBinaryAsync(record.DataFile);
                if (bytes is null)
                {
                    await _store.MarkFailedAsync(record.Id, peer.Id);
                    return;
                }

                data = Convert.ToBase64String(bytes);
            }

            var message = new ClipboardItemMessage(PeerProtocol.ClipboardItem, record.Id, record.OriginDeviceId,
                record.OriginDeviceName, record.Kind, record.Content, record.Meta, record.At, data,
                record.Files?.ToArray(), record.Roots?.ToArray(), record.TotalBytes, _store.ClearAt);

            if (record.Kind == "file" && record.DeliveredTo.Count == 0)
                _store.SetProgress(record.Id, record.TotalBytes > 0 ? 1 : 0);

            await session.SendJsonAsync(message, AppJsonContext.Default.ClipboardItemMessage, cts.Token);

            var ack = record.Kind == "file"
                ? await ServeFileRequestsAsync(session, record, cts.Token)
                : await ReceiveAckAsync(session, cts.Token);

            if (ack is { Ok: true })
            {
                _store.SetProgress(record.Id, record.TotalBytes);
                await _store.MarkDeliveredAsync(record.Id, peer.Id);
            }
            else
                await _store.MarkFailedAsync(record.Id, peer.Id);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not deliver an item to {Device}", peer.Name);
            _presence.MarkOffline(peer.Id);
            await _store.MarkFailedAsync(record.Id, peer.Id);
        }
        finally
        {
            PublishHistory();
            PublishDevices();
        }
    }

    private Task SendClearAsync(SecureSession session, CancellationToken token) =>
        session.SendJsonAsync(new(PeerProtocol.HistoryClear, _store.ClearAt),
            AppJsonContext.Default.HistoryClearMessage, token);

    private async Task<ClipboardAckMessage?> ReceiveAckAsync(SecureSession session, CancellationToken token)
    {
        while (true)
        {
            var frame = await session.ReceiveAsync(token)
                        ?? throw new IOException("The receiver closed the session.");

            switch (FrameProtocol.ReadType(frame))
            {
                case PeerProtocol.ClipboardAck:
                    return JsonSerializer.Deserialize(frame, AppJsonContext.Default.ClipboardAckMessage);

                case PeerProtocol.HistoryClear:
                    var clear = JsonSerializer.Deserialize(frame, AppJsonContext.Default.HistoryClearMessage);
                    if (clear is not null && await _store.ClearIfNewerAsync(clear.At))
                        PublishBoard();
                    break;

                default:
                    throw new IOException("Unexpected frame while waiting for an acknowledgement.");
            }
        }
    }

    private async Task<ClipboardAckMessage?> ServeFileRequestsAsync(SecureSession session, ClipboardRecord record,
        CancellationToken token)
    {
        if (record.Files is not { Count: > 0 } files)
            throw new IOException("The file item has no manifest.");

        while (true)
        {
            var frame = await session.ReceiveAsync(token)
                        ?? throw new IOException("The receiver closed the file transfer.");

            switch (FrameProtocol.ReadType(frame))
            {
                case PeerProtocol.ClipboardAck:
                    return JsonSerializer.Deserialize(frame, AppJsonContext.Default.ClipboardAckMessage);

                case PeerProtocol.HistoryClear:
                    var clear = JsonSerializer.Deserialize(frame, AppJsonContext.Default.HistoryClearMessage);
                    if (clear is not null && await _store.ClearIfNewerAsync(clear.At))
                        PublishBoard();
                    break;

                case PeerProtocol.FileBlobRequest:
                    var request = JsonSerializer.Deserialize(frame, AppJsonContext.Default.FileBlobRequestMessage);
                    if (request is null || request.Id != record.Id || request.Offset < 0 ||
                        request.Length is <= 0 or > PeerProtocol.FileChunkBytes)
                    {
                        await SendBlobErrorAsync(session, record.Id, request?.Hash ?? "", "Invalid blob request.",
                            token);
                        continue;
                    }

                    var entry = files.FirstOrDefault(file =>
                        !file.Directory && string.Equals(file.Hash, request.Hash, StringComparison.Ordinal));

                    if (entry is null || !BlobStore.Exists(request.Hash) ||
                        request.Offset + request.Length > entry.Size)
                    {
                        await SendBlobErrorAsync(session, record.Id, request.Hash, "The blob is not available.",
                            token);
                        continue;
                    }

                    var data = new byte[request.Length];

                    await using (var stream = BlobStore.OpenRead(request.Hash, request.Offset))
                    {
                        var read = 0;
                        while (read < data.Length)
                        {
                            var count = await stream.ReadAsync(data.AsMemory(read), token);
                            if (count == 0)
                                break;

                            read += count;
                        }

                        if (read != data.Length)
                        {
                            await SendBlobErrorAsync(session, record.Id, request.Hash, "The blob is incomplete.",
                                token);
                            continue;
                        }
                    }

                    await session.SendJsonAsync(
                        new(PeerProtocol.FileBlobChunk, record.Id, request.Hash,
                            request.Offset, Convert.ToBase64String(data)),
                        AppJsonContext.Default.FileBlobChunkMessage, token);
                    break;

                default:
                    throw new IOException("Unexpected frame during a file transfer.");
            }
        }
    }

    private static Task SendBlobErrorAsync(SecureSession session, string id, string hash, string error,
        CancellationToken token) =>
        session.SendJsonAsync(new(PeerProtocol.FileBlobError, id, hash, error),
            AppJsonContext.Default.FileBlobErrorMessage, token);

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

        if (message.ClearAt > _store.ClearAt && await _store.ClearIfNewerAsync(message.ClearAt))
            PublishBoard();

        if (message.At <= _store.ClearAt)
        {
            await SendClearAsync(session, token);
            await AcknowledgeAsync(session, message.Id, true, null, token);
            return;
        }

        var peer = _peers.Find(session.PeerDeviceId);
        var originId = message.OriginDeviceId.Length > 0 ? message.OriginDeviceId : session.PeerDeviceId;
        var originName = message.OriginDeviceName.Length > 0
            ? message.OriginDeviceName
            : peer?.Name ?? session.PeerDeviceId;
        var at = message.At > 0 ? message.At : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        ClipboardRecord record;

        if (message.Kind == "file")
        {
            await ReceiveFileItemAsync(session, message, originId, originName, at, token);
            return;
        }

        if (message.Kind == "text")
        {
            if (!_settings.Current.SyncText || message.Content.Length is 0 or > PeerProtocol.MaxClipboardText)
            {
                await AcknowledgeAsync(session, message.Id, false, "Unsupported clipboard text.", token);
                return;
            }

            record = new()
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
                if (await _store.FindAsync(message.Id) is not null)
                {
                    await AcknowledgeAsync(session, message.Id, true, null, token);
                    return;
                }

                dataFile = await ClipboardStore.SaveBinaryAsync(message.Id, png, ".png");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to store a received image");
                await AcknowledgeAsync(session, message.Id, false, "The image could not be stored.", token);
                return;
            }

            record = new()
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

        if (await _store.AddAsync(record))
        {
            Log.Information("Received a clipboard item from {Device}", record.OriginDeviceName);

            if (record.Kind == "image")
            {
                var png = await ClipboardStore.ReadBinaryAsync(record.DataFile);
                if (png is not null)
                    ApplyRemoteImage(png);
            }
            else
                ApplyRemoteText(record.Content);

            PublishBoard();
            _ = RelayAsync(record, session.PeerDeviceId);
        }

        await AcknowledgeAsync(session, message.Id, true, null, token);
    }

    private async Task ReceiveFileItemAsync(SecureSession session, ClipboardItemMessage message, string originId,
        string originName, long at, CancellationToken token)
    {
        using var writeLease = BlobStore.BeginWrite();

        if (!_settings.Current.SyncFiles)
            throw new PeerSecurityException("File syncing is disabled on this device.");

        var files = message.Files ?? [];
        var roots = message.Roots ?? [];

        if (files.Length is 0 or > PeerProtocol.MaxFiles || roots.Length == 0)
            throw new PeerSecurityException("Unsupported clipboard file item.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;

        foreach (var entry in files)
        {
            if (entry.Path.Length == 0 || !seen.Add(entry.Path) ||
                ClipboardFiles.Resolve(Constants.DataDirectory, entry.Path) is null ||
                entry.Size < 0 ||
                !entry.Directory && (entry.Hash.Length != 64 || !entry.Hash.All(Uri.IsHexDigit)))
                throw new PeerSecurityException("The file item contains an unsafe path or hash.");

            total += entry.Size;
            if (total > ClipboardLimits.MaxFileBytes)
                throw new PeerSecurityException("The file item is larger than the configured limit.");
        }

        if (roots.Any(root => !seen.Contains(root) || ClipboardFiles.Resolve(Constants.DataDirectory, root) is null))
            throw new PeerSecurityException("The file item has an unsafe root.");

        if (await _store.FindAsync(message.Id) is not null)
        {
            await AcknowledgeAsync(session, message.Id, true, null, token);
            return;
        }

        var content = message.Content.Length > 0
            ? message.Content
            : roots.Length == 1
                ? Path.GetFileName(roots[0])
                : $"{roots.Length} items";

        var placeholder = new ClipboardRecord
        {
            Id = message.Id,
            OriginDeviceId = originId,
            OriginDeviceName = originName,
            Kind = "file",
            Content = content,
            Meta = message.Meta,
            Files = [.. files],
            Roots = [.. roots],
            TotalBytes = total,
            Progress = 0,
            At = at
        };

        if (!await _store.AddAsync(placeholder))
        {
            await AcknowledgeAsync(session, message.Id, true, null, token);
            return;
        }

        PublishBoard();
        var completed = 0L;

        try
        {
            foreach (var entry in files)
            {
                if (entry.Directory)
                    continue;

                if (entry.Size == 0)
                {
                    await BlobStore.PutEmptyAsync();
                    continue;
                }

                if (BlobStore.Exists(entry.Hash) && BlobStore.Length(entry.Hash) == entry.Size)
                {
                    completed += entry.Size;
                    _store.SetProgress(message.Id, completed);
                    continue;
                }

                if (BlobStore.Exists(entry.Hash))
                    await BlobStore.DeleteAsync(entry.Hash);

                var offset = BlobStore.PartialLength(entry.Hash);
                if (offset > entry.Size)
                {
                    await BlobStore.DeletePartialAsync(entry.Hash);
                    offset = 0;
                }

                completed += offset;
                _store.SetProgress(message.Id, completed);

                while (offset < entry.Size)
                {
                    var length = (int)Math.Min(PeerProtocol.FileChunkBytes, entry.Size - offset);
                    await session.SendJsonAsync(
                        new(PeerProtocol.FileBlobRequest, message.Id, entry.Hash, offset,
                            length), AppJsonContext.Default.FileBlobRequestMessage, token);

                    var frame = await session.ReceiveAsync(token)
                                ?? throw new PeerSecurityException("The file transfer ended early.");

                    switch (FrameProtocol.ReadType(frame))
                    {
                        case PeerProtocol.FileBlobChunk:
                            var chunk = FrameProtocol.Read(frame, AppJsonContext.Default.FileBlobChunkMessage)
                                        ?? throw new PeerSecurityException("A blob chunk could not be read.");

                            if (chunk.Id != message.Id || chunk.Hash != entry.Hash || chunk.Offset != offset)
                                throw new PeerSecurityException("A blob chunk is out of order.");

                            byte[] bytes;
                            try
                            {
                                bytes = Convert.FromBase64String(chunk.Data);
                            }
                            catch (FormatException)
                            {
                                throw new PeerSecurityException("A blob chunk is malformed.");
                            }

                            if (bytes.Length is 0 or > PeerProtocol.FileChunkBytes)
                                throw new PeerSecurityException("A blob chunk has an invalid size.");

                            await BlobStore.AppendPartialAsync(entry.Hash, offset, bytes, token);
                            offset += bytes.Length;
                            completed += bytes.Length;
                            _store.SetProgress(message.Id, completed);
                            PublishProgress();
                            break;

                        case PeerProtocol.FileBlobError:
                            var error = FrameProtocol.Read(frame, AppJsonContext.Default.FileBlobErrorMessage);
                            throw new IOException(error?.Error ?? "The other device could not read a file.");

                        default:
                            throw new PeerSecurityException("Unexpected frame during a file transfer.");
                    }
                }

                if (!await BlobStore.CompletePartialAsync(entry.Hash, entry.Size, token))
                    throw new PeerSecurityException("A file arrived corrupted.");
            }

            var record = await _store.FindAsync(message.Id)
                         ?? throw new PeerSecurityException("The file item disappeared while receiving.");

            await ClipboardFiles.MaterializeAsync(record, token);
            await _store.CompleteFileAsync(message.Id);
            await ApplyRemoteFilesAsync(record);
            PublishBoard();
            _ = RelayAsync(record, session.PeerDeviceId);
            await AcknowledgeAsync(session, message.Id, true, null, token);
        }
        catch
        {
            await _store.RemoveAsync(message.Id);
            PublishBoard();
            throw;
        }
    }

    private static Task AcknowledgeAsync(SecureSession session, string id, bool ok, string? error,
        CancellationToken token) =>
        session.SendJsonAsync(new(PeerProtocol.ClipboardAck, id, ok, error),
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

    private async Task ApplyRemoteFilesAsync(ClipboardRecord record)
    {
        if (!_settings.Current.SyncEnabled || !_settings.Current.SyncFiles ||
            record.Roots is not { Count: > 0 } roots || record.Files is not { Count: > 0 })
            return;

        string folder;
        try
        {
            folder = await ClipboardFiles.MaterializeAsync(record);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not materialise the received files");
            return;
        }

        var paths = new List<string>(roots.Count);

        foreach (var root in roots)
        {
            var path = ClipboardFiles.Resolve(folder, root);
            if (path is not null && (File.Exists(path) || Directory.Exists(path)))
                paths.Add(path);
        }

        if (paths.Count > 0 && !_clipboard.WriteFiles(paths))
            Log.Warning("Could not put the received files back on the clipboard");
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
            {
                if (_store.ClearAt > 0 &&
                    Environment.TickCount64 - _lastClearBroadcast > 300_000)
                {
                    await BroadcastClearAsync(_store.ClearAt);
                    _lastClearBroadcast = Environment.TickCount64;
                }

                await SyncMissingAsync();
            }
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

        foreach (var record in (await _store.SnapshotAsync()).Take(200))
        {
            var targets = peers
                .Where(peer => peer.Id != record.OriginDeviceId && !record.DeliveredTo.Contains(peer.Id))
                .ToArray();

            if (targets.Length == 0)
                continue;

            foreach (var peer in targets)
                await _store.MarkPendingAsync(record.Id, peer.Id);

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
            await _store.MarkPendingAsync(record.Id, peer.Id);

        await Task.WhenAll(peers.Select(peer => DeliverAsync(record, peer)));
    }

    private async Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "devices.get" => await DevicesAsync(context),
        "history.get" => await HistoryAsync(context),
        "image.get" => await GetImageAsync(context),
        "image.copy" => await CopyImageAsync(context),
        "file.copy" => await CopyFilesAsync(context),
        "device.remove" => await RemoveDeviceAsync(context),
        "history.clear" => await ClearHistoryAsync(context),
        "discovery.start" => await StartDiscoveryAsync(context, true),
        "discovery.stop" => await StartDiscoveryAsync(context, false),
        _ => null
    };

    private static Task<NativeResponse?> StartDiscoveryAsync(NativeContext context, bool start)
    {
        if (start)
            DiscoveryService.Instance.BeginSearch();
        else
            DiscoveryService.Instance.EndSearch();

        return Task.FromResult<NativeResponse?>(context.Reply("discovery.result", new OkPayload()));
    }

    private async Task<NativeResponse?> GetImageAsync(NativeContext context)
    {
        var record = await FindImageAsync(context);
        var bytes = record is null ? null : await ClipboardStore.ReadBinaryAsync(record.DataFile);

        if (bytes is null)
            return context.Fail("image.get.result", "The image is not available.");

        return context.Reply("image.get.result",
            new ImagePayload($"data:image/png;base64,{Convert.ToBase64String(bytes)}"));
    }

    private async Task<NativeResponse?> CopyImageAsync(NativeContext context)
    {
        var record = await FindImageAsync(context);
        var bytes = record is null ? null : await ClipboardStore.ReadBinaryAsync(record.DataFile);

        if (bytes is null)
            return context.Fail("image.copy.result", "The image is not available.");

        return _clipboard.WriteImage(bytes)
            ? context.Reply("image.copy.result", new OkPayload())
            : context.Fail("image.copy.result", "The clipboard is busy.");
    }

    private async Task<ClipboardRecord?> FindImageAsync(NativeContext context)
    {
        var id = context.Payload<ImageRequest>()?.ItemId;
        if (id is not { Length: > 0 })
            return null;

        return await _store.FindAsync(id) is { Kind: "image" } record ? record : null;
    }

    private async Task<NativeResponse?> CopyFilesAsync(NativeContext context)
    {
        var id = context.Payload<FileItemRequest>()?.ItemId;
        var record = id is { Length: > 0 } ? await _store.FindAsync(id) : null;

        if (record is not { Kind: "file", Roots: { Count: > 0 } roots, Files: { Count: > 0 } })
            return context.Fail("file.copy.result", "The files are not available.");

        string folder;
        try
        {
            folder = await ClipboardFiles.MaterializeAsync(record);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not materialise a file item");
            return context.Fail("file.copy.result", "The files are not available.");
        }

        var paths = new List<string>(roots.Count);

        foreach (var root in roots)
        {
            var path = ClipboardFiles.Resolve(folder, root);
            if (path is not null && (File.Exists(path) || Directory.Exists(path)))
                paths.Add(path);
        }

        return paths.Count > 0 && _clipboard.WriteFiles(paths)
            ? context.Reply("file.copy.result", new OkPayload())
            : context.Fail("file.copy.result", "The clipboard is busy.");
    }

    private async Task<NativeResponse?> RemoveDeviceAsync(NativeContext context)
    {
        var id = context.Payload<DeviceRemoveRequest>()?.DeviceId;
        if (id is { Length: > 0 })
        {
            await _peers.RemoveAsync(id);
            await _store.ForgetPeerAsync(id);
            PublishBoard();
        }

        return context.Reply("device.remove.result", new OkPayload());
    }

    private async Task<NativeResponse?> ClearHistoryAsync(NativeContext context)
    {
        var at = await _store.ClearAsync();
        _lastClearBroadcast = Environment.TickCount64;
        PublishBoard();
        await BroadcastClearAsync(at);
        return context.Reply("history.clear.result", new OkPayload());
    }

    private async Task BroadcastClearAsync(long at)
    {
        var peers = _peers.All;
        if (peers.Count == 0)
            return;

        await Task.WhenAll(peers.Select(async peer =>
        {
            try
            {
                using var cts = new CancellationTokenSource(PeerProtocol.ConnectionTimeout);
                using var session = await PeerClient.ConnectAsync(peer, cts.Token);
                await session.SendJsonAsync(new(PeerProtocol.HistoryClear, at),
                    AppJsonContext.Default.HistoryClearMessage, cts.Token);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not send the clear to {Device}", peer.Name);
            }
        }));
    }

    private void PublishBoard() => _ = PublishBoardAsync();

    private async Task PublishBoardAsync()
    {
        await Task.WhenAll(PublishHistoryAsync(), PublishDevicesAsync());
    }

    private void PublishDevices() => _ = PublishDevicesAsync();

    private void PublishHistory() => _ = PublishHistoryAsync();

    private async Task PublishDevicesAsync()
    {
        var payload = await BuildDevicesAsync();
        WindowManager.Instance.NotifyMainWindow("devices.changed", payload);
    }

    private async Task PublishHistoryAsync()
    {
        var payload = await BuildHistoryAsync();
        WindowManager.Instance.NotifyMainWindow("history.changed", payload);
    }

    private void PublishProgress()
    {
        var now = Environment.TickCount64;
        if (now - _lastProgressPublish < 100)
            return;

        _lastProgressPublish = now;
        PublishHistory();
    }

    private async Task<DevicesPayload> BuildDevicesAsync()
    {
        var items = await _store.SnapshotAsync();
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

        return new(self, peers, discovered);
    }

    private async Task<HistoryPayload> BuildHistoryAsync()
    {
        var peers = _peers.All.Count;
        var items = (await _store.SnapshotAsync()).Select(record => ToPayload(record, peers)).ToArray();
        return new(items);
    }

    private async Task<NativeResponse?> DevicesAsync(NativeContext context) =>
        context.Reply("devices.get.result", await BuildDevicesAsync());

    private async Task<NativeResponse?> HistoryAsync(NativeContext context) =>
        context.Reply("history.get.result", await BuildHistoryAsync());

    private ClipboardItemPayload ToPayload(ClipboardRecord record, int peers)
    {
        var local = record.OriginDeviceId == _identity.Id;
        var reached = local ? record.DeliveredTo.Count : 0;

        string status;
        if (!local)
            status = record.Kind == "file" && record.Progress < record.TotalBytes
                ? "transferring"
                : "received";
        else if (record.FailedFor.Count > 0)
            status = "failed";
        else if (record.PendingFor.Count > 0)
            status = record.Kind == "file" && record.Progress > 0 && record.Progress < record.TotalBytes
                ? "transferring"
                : reached > 0
                    ? "transferring"
                    : "waiting";
        else if (peers == 0 || reached >= peers)
            status = "synced";
        else
            status = "waiting";

        return new(record.Id, record.OriginDeviceId, record.OriginDeviceName, record.Kind,
            record.Content, record.Meta, record.At, local ? "out" : "in", status, reached, peers,
            record.Files?.ToArray(), record.TotalBytes, record.Progress);
    }
}