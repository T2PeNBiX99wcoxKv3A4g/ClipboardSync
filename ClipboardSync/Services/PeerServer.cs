using System.Net;
using System.Net.Sockets;
using ClipboardSync.Messaging;
using ClipboardSync.Security;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     The encrypted peer listener. It accepts pairing requests and authenticated sessions, and
///     answers TCP discovery probes. A session hands its stream to <see cref="SyncService" /> for the
///     lifetime of the connection.
/// </summary>
internal sealed class PeerServer : Singleton<PeerServer>
{
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            Stop();
            return;
        }

        if (_listener is not null)
            return;

        var dual = Try.Run(() =>
        {
            var listener = new TcpListener(IPAddress.IPv6Any, PeerProtocol.SyncPort);
            listener.Server.DualMode = true;
            listener.Start();
            Log.Information("Peer listener bound to IPv4 and IPv6 on port {Port}", PeerProtocol.SyncPort);
            return listener;
        });

        if (dual.Exception is null && dual.Value is { } dualListener)
        {
            StartLoop(dualListener);
            return;
        }

        Log.Debug(dual.Exception, "A dual-stack peer listener is unavailable; binding IPv4 only");

        var ipv4 = Try.Run(() =>
        {
            var listener = new TcpListener(IPAddress.Any, PeerProtocol.SyncPort);
            listener.Start();
            Log.Information("Peer listener bound to IPv4 on port {Port}", PeerProtocol.SyncPort);
            return listener;
        });

        if (ipv4.Exception is null && ipv4.Value is { } ipv4Listener)
        {
            StartLoop(ipv4Listener);
            return;
        }

        Log.Warning(ipv4.Exception, "Failed to start the peer listener on port {Port}", PeerProtocol.SyncPort);
    }

    private void StartLoop(TcpListener listener)
    {
        var cts = new CancellationTokenSource();
        _listener = listener;
        _cancellation = cts;
        _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;

        Try.Run(() => _listener?.Stop()).OnFailure(ex => Log.Debug(ex, "Failed to stop the peer listener"));
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "The peer listener failed to accept a connection");
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, token), token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            cts.CancelAfter(PeerProtocol.PairTimeout + PeerProtocol.ConnectionTimeout);
            client.NoDelay = true;

            var stream = client.GetStream();
            (await Try.Run(async () =>
            {
                var frame = await FrameProtocol.ReadFrameAsync(stream, cts.Token);
                if (frame is null)
                    return;

                switch (FrameProtocol.ReadType(frame))
                {
                    case PeerProtocol.PairRequest:
                        await HandlePairingAsync(stream, frame, client, cts.Token);
                        break;

                    case PeerProtocol.SessionHello:
                        await HandleSessionAsync(stream, frame, client, cts.Token, token);
                        break;

                    case PeerProtocol.Discover:
                        await WriteAnnounceAsync(stream, cts.Token);
                        break;

                    default:
                        await WriteStatusAsync(stream, false, "Unsupported peer message.", cts.Token);
                        break;
                }
            })).OnFailure(ex => Log.Debug(ex, "Failed to handle a peer connection"));
        }
    }

    private async Task HandlePairingAsync(NetworkStream stream, byte[] frame, TcpClient client,
        CancellationToken token)
    {
        var peer = await PairingHandshake.AcceptAsync(stream, DeviceIdentity.Instance,
            SettingsService.Instance.Current.DeviceName, PeerProtocol.SyncPort, HostOf(client), frame,
            PairingService.Instance.RequestApprovalAsync, token);

        if (peer is null)
            return;

        await PeerStore.Instance.AddOrUpdateAsync(peer);
        Log.Information("Paired with {Device} ({Id})", peer.Name, peer.Id);
        PairingService.Instance.OnPeerPaired(peer);
    }

    private async Task HandleSessionAsync(NetworkStream stream, byte[] frame, TcpClient client,
        CancellationToken handshakeToken, CancellationToken sessionToken)
    {
        using var session = await PeerHandshake.AcceptAsync(stream, DeviceIdentity.Instance,
            id => PeerStore.Instance.Find(id), frame, handshakeToken, client);

        await SyncService.Instance.HandleSessionAsync(session, sessionToken);
    }

    private static Task WriteAnnounceAsync(NetworkStream stream, CancellationToken token)
    {
        var identity = DeviceIdentity.Instance;
        return FrameProtocol.WriteAsync(stream,
            new(PeerProtocol.Announce, identity.Id, SettingsService.Instance.Current.DeviceName,
                PeerProtocol.SyncPort, identity.PublicKeyBase64),
            AppJsonContext.Default.DiscoveryMessage, token);
    }

    private static Task WriteStatusAsync(NetworkStream stream, bool ok, string? error, CancellationToken token) =>
        FrameProtocol.WriteAsync(stream, new(PeerProtocol.Status, ok, error),
            AppJsonContext.Default.SecureStatusMessage, token);

    private static string HostOf(TcpClient client)
    {
        var address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.ToString();
    }
}