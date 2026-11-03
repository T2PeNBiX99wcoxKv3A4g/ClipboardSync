using System.Text.Json.Serialization.Metadata;
using ClipboardSync.Messaging;

namespace ClipboardSync.Security;

/// <summary>
///     An authenticated, encrypted stream between two paired devices. The handshake produced a fresh
///     X25519 secret, HKDF derived the session key, and every frame is a ChaCha20-Poly1305 message
///     bound to the handshake transcript.
/// </summary>
internal sealed class SecureSession : IDisposable
{
    private readonly byte[] _associatedData;
    private readonly byte[] _key;
    private readonly IDisposable? _owner;
    private readonly Stream _stream;

    internal SecureSession(Stream stream, byte[] key, byte[] associatedData, string peerDeviceId, bool isInitiator,
        IDisposable? owner = null)
    {
        _stream = stream;
        _key = key;
        _associatedData = associatedData;
        _owner = owner;
        PeerDeviceId = peerDeviceId;
        IsInitiator = isInitiator;
    }

    public string PeerDeviceId { get; }

    public bool IsInitiator { get; }

    /// <summary>Encrypts one payload and writes it as a frame.</summary>
    public async Task SendAsync(byte[] plaintext, CancellationToken token)
    {
        var nonce = ClipboardCrypto.RandomBytes(ClipboardCrypto.NonceSize);
        var ciphertext = ClipboardCrypto.Encrypt(_key, nonce, plaintext, _associatedData);

        await FrameProtocol.WriteAsync(_stream,
            new SecureDataMessage(PeerProtocol.SecureData, ClipboardCrypto.ToBase64(nonce),
                ClipboardCrypto.ToBase64(ciphertext)),
            AppJsonContext.Default.SecureDataMessage, token);
    }

    /// <summary>Reads and decrypts one frame; null means the peer closed the stream.</summary>
    public async Task<byte[]?> ReceiveAsync(CancellationToken token)
    {
        var frame = await FrameProtocol.ReadFrameAsync(_stream, token);
        if (frame is null)
            return null;

        if (FrameProtocol.ReadType(frame) != PeerProtocol.SecureData)
            throw new PeerSecurityException("Unexpected frame in an encrypted session.");

        var message = FrameProtocol.Read(frame, AppJsonContext.Default.SecureDataMessage)
                      ?? throw new PeerSecurityException("The encrypted frame could not be read.");
        var nonce = ClipboardCrypto.FromBase64(message.Nonce);
        var ciphertext = ClipboardCrypto.FromBase64(message.Ciphertext);

        if (nonce is not { Length: ClipboardCrypto.NonceSize } || ciphertext is null)
            throw new PeerSecurityException("The encrypted frame is malformed.");

        return ClipboardCrypto.TryDecrypt(_key, nonce, ciphertext, _associatedData)
               ?? throw new PeerSecurityException("The encrypted frame could not be decrypted.");
    }

    public async Task SendJsonAsync<T>(T payload, JsonTypeInfo<T> typeInfo, CancellationToken token)
    {
        var plaintext = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload, typeInfo);
        await SendAsync(plaintext, token);
    }

    public async Task<T?> ReceiveJsonAsync<T>(JsonTypeInfo<T> typeInfo, CancellationToken token) where T : class
    {
        var plaintext = await ReceiveAsync(token);
        return plaintext is null ? null : System.Text.Json.JsonSerializer.Deserialize(plaintext, typeInfo);
    }

    /// <summary>Closes the socket that produced this session, when the session owns one.</summary>
    public void Dispose() => _owner?.Dispose();
}

/// <summary>A peer sent something that did not authenticate or did not follow the protocol.</summary>
internal sealed class PeerSecurityException(string message) : Exception(message);
