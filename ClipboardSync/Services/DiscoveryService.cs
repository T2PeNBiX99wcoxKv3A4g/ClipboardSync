using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Security;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     Finds ClipboardSync devices on the local network over UDP broadcast. Discovery is only a
///     convenience: the pairing handshake still pins the public key before anything is trusted.
/// </summary>
internal sealed class DiscoveryService : Singleton<DiscoveryService>
{
    private static readonly TimeSpan QueryInterval = TimeSpan.FromMilliseconds(1_500);
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(8);

    private readonly ConcurrentDictionary<string, (DiscoveredDevice Device, long SeenAt)> _found =
        new(StringComparer.Ordinal);

    private readonly Lock _gate = new();
    private string _deviceName = "";
    private string _published = "";
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _receiveCts;
    private bool _searching;
    private UdpClient? _socket;

    public void Apply(bool enabled, string deviceName)
    {
        lock (_gate)
        {
            _deviceName = deviceName;

            if (enabled)
            {
                if (_socket is null)
                    StartSocket();
                return;
            }

            StopQuery();
            StopSocket();
            _searching = false;
            _found.Clear();
            Publish();
        }
    }

    public void BeginSearch()
    {
        lock (_gate)
        {
            _searching = true;
            _found.Clear();
            StopQuery();
            _queryCts = new();
            _ = Task.Run(() => QueryLoopAsync(_queryCts.Token));
            Log.Information("Searching for devices on the local network");
        }
    }

    public void EndSearch()
    {
        lock (_gate)
        {
            _searching = false;
            StopQuery();
            _found.Clear();
            Publish();
        }
    }

    public IReadOnlyList<DiscoveredDevice> Snapshot()
    {
        var cutoff = Environment.TickCount64 - (long)FreshFor.TotalMilliseconds;

        return
        [
            .. _found.Values
                .Where(entry => entry.SeenAt >= cutoff)
                .Select(entry => entry.Device)
                .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private void StartSocket()
    {
        var cts = _receiveCts = new();

        Try.Run(() =>
        {
            var socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, PeerProtocol.DiscoveryPort));
            _socket = socket;
            _ = Task.Run(() => ReceiveLoopAsync(socket, cts.Token), cts.Token);
            Log.Information("Device discovery is listening on UDP {Port}", PeerProtocol.DiscoveryPort);
        }).OnFailure(ex => Log.Warning(ex, "Failed to open the discovery socket"));
    }

    private void StopSocket()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        Try.Run(() => _socket?.Close()).OnFailure(ex => Log.Debug(ex, "Failed to close the discovery socket"));
        _socket = null;
    }

    private void StopQuery()
    {
        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = null;
    }

    private async Task ReceiveLoopAsync(UdpClient socket, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(token);
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
                Log.Debug(ex, "A discovery datagram could not be read");
                continue;
            }

            (await Try.Run(() => HandleDatagramAsync(socket, result, token)))
                .OnFailure(ex => Log.Debug(ex, "A discovery datagram could not be handled"));
        }
    }

    private async Task HandleDatagramAsync(UdpClient socket, UdpReceiveResult result, CancellationToken token)
    {
        var type = FrameProtocol.ReadType(result.Buffer);
        var host = HostOf(result.RemoteEndPoint);

        switch (type)
        {
            case PeerProtocol.Discover:
                await SendAnnounceAsync(socket, result.RemoteEndPoint, token);
                return;

            case PeerProtocol.Announce:
            {
                var announce = JsonSerializer.Deserialize(result.Buffer, AppJsonContext.Default.DiscoveryMessage);
                if (announce is not null && _searching && announce.DeviceId != DeviceIdentity.Instance.Id)
                    Record(announce, host);

                return;
            }
        }
    }

    private async Task QueryLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            SendQuery();
            Publish();

            try
            {
                await Task.Delay(QueryInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void SendQuery()
    {
        if (_socket is not { } socket)
            return;

        var frame = JsonSerializer.SerializeToUtf8Bytes(new(PeerProtocol.Discover,
                DeviceIdentity.Instance.Id, _deviceName, PeerProtocol.SyncPort, DeviceIdentity.Instance.PublicKeyBase64),
            AppJsonContext.Default.DiscoveryMessage);

        TrySend(socket, frame, new(IPAddress.Broadcast, PeerProtocol.DiscoveryPort));

        foreach (var address in BroadcastAddresses())
            TrySend(socket, frame, new(address, PeerProtocol.DiscoveryPort));
    }

    private static Task SendAnnounceAsync(UdpClient socket, IPEndPoint target, CancellationToken token) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new(PeerProtocol.Announce,
                DeviceIdentity.Instance.Id, SettingsService.Instance.Current.DeviceName, PeerProtocol.SyncPort,
                DeviceIdentity.Instance.PublicKeyBase64),
            AppJsonContext.Default.DiscoveryMessage), target, token).AsTask();

    private void Record(DiscoveryMessage announce, string host)
    {
        var device = new DiscoveredDevice(announce.DeviceId, announce.DeviceName, host, announce.Port);

        _found[announce.DeviceId] = (device, Environment.TickCount64);
        Publish();
    }

    private void Publish()
    {
        var devices = Snapshot();
        var signature = string.Join('\n',
            devices.Select(device => $"{device.Id}|{device.Name}|{device.Host}|{device.Port}"));
        if (signature == _published)
            return;

        _published = signature;
        if (!SyncService.IsInitialized)
            return;

        Try.Run(() => SyncService.Instance.OnDiscoveryChanged())
            .OnFailure(ex => Log.Debug(ex, "Failed to publish discovered devices"));
    }

    private void TrySend(UdpClient socket, byte[] frame, IPEndPoint target) =>
        Try.Run(() => socket.Send(frame, frame.Length, target))
            .OnFailure(ex => Log.Debug(ex, "Failed to send a discovery query to {Target}", target));

    private static IEnumerable<IPAddress> BroadcastAddresses()
    {
        foreach (var nic in UpInterfaces())
        foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
        {
            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                continue;

            var address = unicast.Address.GetAddressBytes();
            var mask = unicast.IPv4Mask.GetAddressBytes();
            var broadcast = new byte[4];
            for (var i = 0; i < 4; i++)
                broadcast[i] = (byte)(address[i] | ~mask[i]);

            yield return new(broadcast);
        }
    }

    private static IEnumerable<NetworkInterface> UpInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(nic =>
            nic.OperationalStatus == OperationalStatus.Up &&
            nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            nic.Supports(NetworkInterfaceComponent.IPv4));

    private static string HostOf(IPEndPoint endpoint)
    {
        var address = endpoint.Address;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.ToString();
    }
}