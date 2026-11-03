using System.Text;
using ClipboardSync.Messaging;
using ClipboardSync.Models;

namespace ClipboardSync.Security;

/// <summary>
///     The pairing handshake from URLCheck's forwarding protocol. Both sides prove ownership of their
///     Ed25519 key, derive a short code from the same transcript, and the user compares it before the
///     target accepts. Public keys are pinned from then on, so a later impersonation fails.
/// </summary>
internal static class PairingHandshake
{
    /// <summary>Pairs with the device at the other end of <paramref name="stream" />.</summary>
    public static async Task<PairedDevice> InitiateAsync(Stream stream, DeviceIdentity identity,
        string localDeviceName, int localPort, string host, Func<string, PairedDevice, Task> onChallenge,
        CancellationToken token)
    {
        var nonce = ClipboardCrypto.RandomBytes(ClipboardCrypto.HandshakeNonceSize);
        var requestSignature = ClipboardCrypto.Sign(identity.PrivateKey, ClipboardCrypto.Transcript(
            "ClipboardSync.Pair.Request.v1", identity.PublicKey, Utf8(identity.Id), nonce));

        await FrameProtocol.WriteAsync(stream,
            new PairRequestMessage(PeerProtocol.PairRequest, identity.Id, localDeviceName, localPort,
                identity.PublicKeyBase64, ClipboardCrypto.ToBase64(nonce),
                ClipboardCrypto.ToBase64(requestSignature)),
            AppJsonContext.Default.PairRequestMessage, token);

        var replyFrame = await FrameProtocol.ReadFrameAsync(stream, token)
                         ?? throw new PeerSecurityException("The peer closed the pairing.");
        var replyType = FrameProtocol.ReadType(replyFrame);

        if (replyType == PeerProtocol.Status)
        {
            var status = FrameProtocol.Read(replyFrame, AppJsonContext.Default.SecureStatusMessage);
            throw new PeerSecurityException(status?.Error ?? "The peer refused the pairing.");
        }

        if (replyType != PeerProtocol.PairChallenge)
            throw new PeerSecurityException("The peer sent an unexpected pairing message.");

        var challenge = FrameProtocol.Read(replyFrame, AppJsonContext.Default.PairChallengeMessage)
                        ?? throw new PeerSecurityException("The pairing challenge could not be read.");

        var peerKey = ClipboardCrypto.FromBase64(challenge.PublicKey);
        if (peerKey is not { Length: ClipboardCrypto.KeySize })
            throw new PeerSecurityException("The peer has no usable public key.");

        var transcript = ClipboardCrypto.Transcript("ClipboardSync.Pair.v1",
            identity.PublicKey, Utf8(identity.Id), peerKey, Utf8(challenge.DeviceId), nonce);

        var peer = new PairedDevice
        {
            Id = challenge.DeviceId,
            Name = challenge.DeviceName,
            Host = host,
            Port = challenge.Port,
            PublicKey = challenge.PublicKey
        };

        await onChallenge(ClipboardCrypto.PairingCode(transcript), peer);

        var decisionFrame = await FrameProtocol.ReadFrameAsync(stream, token)
                            ?? throw new PeerSecurityException("The peer closed the pairing.");

        switch (FrameProtocol.ReadType(decisionFrame))
        {
            case PeerProtocol.PairReject:
                var reject = FrameProtocol.Read(decisionFrame, AppJsonContext.Default.PairRejectMessage);
                throw new PeerSecurityException(reject?.Reason ?? "The pairing was rejected.");

            case PeerProtocol.Status:
                var status = FrameProtocol.Read(decisionFrame, AppJsonContext.Default.SecureStatusMessage);
                throw new PeerSecurityException(status?.Error ?? "The pairing failed.");

            case PeerProtocol.PairAccept:
            {
                var accept = FrameProtocol.Read(decisionFrame, AppJsonContext.Default.PairAcceptMessage)
                             ?? throw new PeerSecurityException("The pairing answer could not be read.");
                var signature = ClipboardCrypto.FromBase64(accept.Signature);
                var expected = ClipboardCrypto.Transcript("ClipboardSync.Pair.Accept.v1", transcript);

                if (signature is null || !ClipboardCrypto.Verify(peerKey, expected, signature))
                    throw new PeerSecurityException("The peer could not confirm the pairing.");

                return peer;
            }

            default:
                throw new PeerSecurityException("The peer sent an unexpected pairing message.");
        }
    }

    /// <summary>
    ///     Accepts a pairing request. The challenge is sent before <paramref name="approve" /> runs, so
    ///     the other device can show the same code while the user decides.
    /// </summary>
    public static async Task<PairedDevice?> AcceptAsync(Stream stream, DeviceIdentity identity,
        string localDeviceName, int localPort, string peerHost, byte[] requestFrame,
        Func<PairingPrompt, CancellationToken, Task<bool>> approve, CancellationToken token)
    {
        if (FrameProtocol.ReadType(requestFrame) != PeerProtocol.PairRequest)
            throw new PeerSecurityException("The peer did not send a pairing request.");

        var request = FrameProtocol.Read(requestFrame, AppJsonContext.Default.PairRequestMessage)
                      ?? throw new PeerSecurityException("The pairing request could not be read.");

        var peerKey = ClipboardCrypto.FromBase64(request.PublicKey);
        var nonce = ClipboardCrypto.FromBase64(request.Nonce);
        var signature = ClipboardCrypto.FromBase64(request.Signature);

        if (peerKey is not { Length: ClipboardCrypto.KeySize } ||
            nonce is not { Length: ClipboardCrypto.HandshakeNonceSize } || signature is null)
        {
            await WriteStatusAsync(stream, false, "The pairing request is malformed.", token);
            return null;
        }

        var requestTranscript = ClipboardCrypto.Transcript("ClipboardSync.Pair.Request.v1",
            peerKey, Utf8(request.DeviceId), nonce);

        if (!ClipboardCrypto.Verify(peerKey, requestTranscript, signature))
        {
            await WriteStatusAsync(stream, false, "The peer could not prove its identity.", token);
            return null;
        }

        var transcript = ClipboardCrypto.Transcript("ClipboardSync.Pair.v1",
            peerKey, Utf8(request.DeviceId), identity.PublicKey, Utf8(identity.Id), nonce);

        await FrameProtocol.WriteAsync(stream,
            new PairChallengeMessage(PeerProtocol.PairChallenge, identity.Id, localDeviceName, localPort,
                identity.PublicKeyBase64),
            AppJsonContext.Default.PairChallengeMessage, token);

        var prompt = new PairingPrompt(request.DeviceId, request.DeviceName, request.PublicKey,
            ClipboardCrypto.PairingCode(transcript));

        if (!await approve(prompt, token))
        {
            await FrameProtocol.WriteAsync(stream,
                new PairRejectMessage(PeerProtocol.PairReject, "The pairing was rejected."),
                AppJsonContext.Default.PairRejectMessage, token);
            return null;
        }

        var accept = ClipboardCrypto.Sign(identity.PrivateKey,
            ClipboardCrypto.Transcript("ClipboardSync.Pair.Accept.v1", transcript));

        await FrameProtocol.WriteAsync(stream,
            new PairAcceptMessage(PeerProtocol.PairAccept, ClipboardCrypto.ToBase64(accept)),
            AppJsonContext.Default.PairAcceptMessage, token);

        return new PairedDevice
        {
            Id = request.DeviceId,
            Name = request.DeviceName,
            Host = peerHost,
            Port = request.Port,
            PublicKey = request.PublicKey
        };
    }

    private static Task WriteStatusAsync(Stream stream, bool ok, string? error, CancellationToken token) =>
        FrameProtocol.WriteAsync(stream, new SecureStatusMessage(PeerProtocol.Status, ok, error),
            AppJsonContext.Default.SecureStatusMessage, token);

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
