using System.Net.Sockets;
using ClipboardSync.Models;
using ClipboardSync.Security;

namespace ClipboardSync.Services;

/// <summary>Connects to a paired device, or starts a pairing with a discovered address.</summary>
internal static class PeerClient
{
    /// <summary>Pairs with the device at <paramref name="host" />:<paramref name="port" />.</summary>
    public static async Task<PairedDevice> PairAsync(string host, int port, string deviceName,
        Func<string, PairedDevice, Task> onChallenge, CancellationToken token)
    {
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, token);

        return await PairingHandshake.InitiateAsync(client.GetStream(), DeviceIdentity.Instance, deviceName,
            PeerProtocol.SyncPort, host, onChallenge, token);
    }

    /// <summary>Opens an authenticated session, trying every address the peer has.</summary>
    public static async Task<SecureSession> ConnectAsync(PairedDevice peer, CancellationToken token)
    {
        PeerSecurityException? failure = null;

        foreach (var address in peer.AllAddresses().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var client = new TcpClient { NoDelay = true };

            try
            {
                await client.ConnectAsync(address, peer.Port, token);
                return await PeerHandshake.ConnectAsync(client.GetStream(), DeviceIdentity.Instance, peer, token,
                    client);
            }
            catch (OperationCanceledException)
            {
                client.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                client.Dispose();
                failure = ex as PeerSecurityException
                          ?? new PeerSecurityException($"The peer at {address}:{peer.Port} did not answer ({ex.Message}).");
            }
        }

        throw failure ?? new PeerSecurityException("The device is unreachable.");
    }

    /// <summary>Whether the device answers an authenticated handshake right now.</summary>
    public static async Task<bool> ProbeAsync(PairedDevice peer, CancellationToken token)
    {
        try
        {
            using var session = await ConnectAsync(peer, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
