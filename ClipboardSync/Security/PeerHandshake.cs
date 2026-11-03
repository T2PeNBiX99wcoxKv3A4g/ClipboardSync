using System.Text;
using ClipboardSync.Messaging;
using ClipboardSync.Models;

namespace ClipboardSync.Security;

/// <summary>
///     The initiating and accepting halves of the encrypted session handshake. It is deliberately
///     transport-agnostic: any connected <see cref="Stream" /> can be handed in, so TCP, QUIC or a
///     test pipe all use the same authentication and key agreement.
/// </summary>
internal static class PeerHandshake
{
    /// <summary>Connects to a pinned peer and returns the encrypted session.</summary>
    public static async Task<SecureSession> ConnectAsync(Stream stream, DeviceIdentity identity, PairedDevice peer,
        CancellationToken token, IDisposable? owner = null)
    {
        var peerKey = ClipboardCrypto.FromBase64(peer.PublicKey);
        if (peerKey is not { Length: ClipboardCrypto.KeySize })
            throw new PeerSecurityException("The peer has no usable public key.");

        var (ephemeralPrivate, ephemeralPublic) = ClipboardCrypto.GenerateEphemeralKeyPair();
        var nonce = ClipboardCrypto.RandomBytes(ClipboardCrypto.HandshakeNonceSize);

        var helloSignature = ClipboardCrypto.Sign(identity.PrivateKey, ClipboardCrypto.Transcript(
            "ClipboardSync.Session.Hello.v1", identity.PublicKey, Utf8(identity.Id), peerKey, Utf8(peer.Id),
            ephemeralPublic, nonce));

        await FrameProtocol.WriteAsync(stream,
            new(PeerProtocol.SessionHello, identity.Id,
                ClipboardCrypto.ToBase64(ephemeralPublic), ClipboardCrypto.ToBase64(nonce),
                ClipboardCrypto.ToBase64(helloSignature)),
            AppJsonContext.Default.SessionHelloMessage, token);

        var replyFrame = await FrameProtocol.ReadFrameAsync(stream, token)
                         ?? throw new PeerSecurityException("The peer closed the handshake.");
        var replyType = FrameProtocol.ReadType(replyFrame);

        if (replyType == PeerProtocol.Status)
        {
            var status = FrameProtocol.Read(replyFrame, AppJsonContext.Default.SecureStatusMessage);
            throw new PeerSecurityException(status?.Error ?? "The peer refused the session.");
        }

        if (replyType != PeerProtocol.SessionReply)
            throw new PeerSecurityException("The peer sent an unexpected handshake message.");

        var reply = FrameProtocol.Read(replyFrame, AppJsonContext.Default.SessionReplyMessage)
                    ?? throw new PeerSecurityException("The handshake reply could not be read.");

        var peerEphemeral = ClipboardCrypto.FromBase64(reply.EphemeralKey);
        var peerNonce = ClipboardCrypto.FromBase64(reply.Nonce);
        var peerSignature = ClipboardCrypto.FromBase64(reply.Signature);

        if (peerEphemeral is not { Length: ClipboardCrypto.KeySize } ||
            peerNonce is not { Length: ClipboardCrypto.HandshakeNonceSize } || peerSignature is null)
            throw new PeerSecurityException("The handshake reply is malformed.");

        var expected = ClipboardCrypto.Transcript("ClipboardSync.Session.Reply.v1",
            peerKey, Utf8(peer.Id), identity.PublicKey, Utf8(identity.Id),
            ephemeralPublic, peerEphemeral, nonce, peerNonce);

        if (!ClipboardCrypto.Verify(peerKey, expected, peerSignature))
            throw new PeerSecurityException("The peer could not prove its identity.");

        var shared = ClipboardCrypto.SharedSecret(ephemeralPrivate, peerEphemeral);
        var key = ClipboardCrypto.DeriveKey(shared, [.. nonce, .. peerNonce], "ClipboardSync.Session.v1");
        var associatedData = ClipboardCrypto.Transcript("ClipboardSync.Session.Data.v1",
            ephemeralPublic, peerEphemeral, nonce, peerNonce);

        return new(stream, key, associatedData, peer.Id, true, owner);
    }

    /// <summary>Accepts a session from a peer that is already paired.</summary>
    public static async Task<SecureSession> AcceptAsync(Stream stream, DeviceIdentity identity,
        Func<string, PairedDevice?> findPeer, byte[] helloFrame, CancellationToken token, IDisposable? owner = null)
    {
        if (FrameProtocol.ReadType(helloFrame) != PeerProtocol.SessionHello)
            throw new PeerSecurityException("The peer did not start an encrypted handshake.");

        var hello = FrameProtocol.Read(helloFrame, AppJsonContext.Default.SessionHelloMessage)
                    ?? throw new PeerSecurityException("The handshake could not be read.");

        var peer = findPeer(hello.DeviceId)
                   ?? throw new PeerSecurityException("The device is not paired.");
        var peerKey = ClipboardCrypto.FromBase64(peer.PublicKey);
        var ephemeral = ClipboardCrypto.FromBase64(hello.EphemeralKey);
        var nonce = ClipboardCrypto.FromBase64(hello.Nonce);
        var signature = ClipboardCrypto.FromBase64(hello.Signature);

        if (peerKey is not { Length: ClipboardCrypto.KeySize } ||
            ephemeral is not { Length: ClipboardCrypto.KeySize } ||
            nonce is not { Length: ClipboardCrypto.HandshakeNonceSize } || signature is null)
            throw new PeerSecurityException("The handshake is malformed.");

        var expected = ClipboardCrypto.Transcript("ClipboardSync.Session.Hello.v1",
            peerKey, Utf8(peer.Id), identity.PublicKey, Utf8(identity.Id), ephemeral, nonce);

        if (!ClipboardCrypto.Verify(peerKey, expected, signature))
            throw new PeerSecurityException("The peer could not prove its identity.");

        var (replyPrivate, replyPublic) = ClipboardCrypto.GenerateEphemeralKeyPair();
        var replyNonce = ClipboardCrypto.RandomBytes(ClipboardCrypto.HandshakeNonceSize);

        var replySignature = ClipboardCrypto.Sign(identity.PrivateKey, ClipboardCrypto.Transcript(
            "ClipboardSync.Session.Reply.v1", identity.PublicKey, Utf8(identity.Id), peerKey, Utf8(peer.Id),
            ephemeral, replyPublic, nonce, replyNonce));

        await FrameProtocol.WriteAsync(stream,
            new(PeerProtocol.SessionReply, ClipboardCrypto.ToBase64(replyPublic),
                ClipboardCrypto.ToBase64(replyNonce), ClipboardCrypto.ToBase64(replySignature)),
            AppJsonContext.Default.SessionReplyMessage, token);

        var shared = ClipboardCrypto.SharedSecret(replyPrivate, ephemeral);
        var key = ClipboardCrypto.DeriveKey(shared, [.. nonce, .. replyNonce], "ClipboardSync.Session.v1");
        var associatedData = ClipboardCrypto.Transcript("ClipboardSync.Session.Data.v1",
            ephemeral, replyPublic, nonce, replyNonce);

        return new(stream, key, associatedData, peer.Id, false, owner);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}