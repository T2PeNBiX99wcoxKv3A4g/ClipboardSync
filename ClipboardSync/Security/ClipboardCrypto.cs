using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Security;

/// <summary>
///     The primitives behind a peer session, copied from URLCheck's forwarding protocol: Ed25519
///     identities, X25519 ephemeral key agreement, HKDF-SHA256 key derivation and ChaCha20-Poly1305
///     authenticated encryption. The pairing code never becomes a key; the two devices prove who they
///     are with pinned signatures and show the same short digest to the user.
/// </summary>
internal static class ClipboardCrypto
{
    public const int KeySize = 32;

    /// <summary>Nonce length ChaCha20-Poly1305 uses.</summary>
    public const int NonceSize = 12;

    /// <summary>Length of the random nonces exchanged during a handshake.</summary>
    public const int HandshakeNonceSize = 16;

    private static readonly SecureRandom Random = new();

    public static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.NextBytes(bytes);
        return bytes;
    }

    public static string ToBase64(byte[] value) => Convert.ToBase64String(value);

    public static byte[]? FromBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Builds the byte string a signature covers. Every part is length-prefixed so two different
    ///     groupings can never produce the same transcript.
    /// </summary>
    public static byte[] Transcript(string label, params byte[][] parts)
    {
        using var buffer = new MemoryStream();
        var labelBytes = Encoding.UTF8.GetBytes(label);
        buffer.Write(labelBytes);
        foreach (var part in parts)
        {
            buffer.WriteByte((byte)(part.Length >> 24));
            buffer.WriteByte((byte)(part.Length >> 16));
            buffer.WriteByte((byte)(part.Length >> 8));
            buffer.WriteByte((byte)part.Length);
            buffer.Write(part);
        }

        return buffer.ToArray();
    }

    public static byte[] Sign(byte[] privateKey, byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKey, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    // A malformed key or signature is a failed verification, never a crash.
    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature) =>
        Try.Run(() =>
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }).GetOrElse(_ => false);

    /// <summary>A fresh X25519 key pair, used once per connection so every session has its own key.</summary>
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateEphemeralKeyPair()
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(Random));
        var pair = generator.GenerateKeyPair();

        return (((X25519PrivateKeyParameters)pair.Private).GetEncoded(),
            ((X25519PublicKeyParameters)pair.Public).GetEncoded());
    }

    public static byte[] SharedSecret(byte[] privateKey, byte[] peerPublicKey)
    {
        var agreement = new X25519Agreement();
        agreement.Init(new X25519PrivateKeyParameters(privateKey, 0));

        var shared = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(new X25519PublicKeyParameters(peerPublicKey, 0), shared, 0);
        return shared;
    }

    public static byte[] DeriveKey(byte[] sharedSecret, byte[] salt, string info)
    {
        var hkdf = new HkdfBytesGenerator(new Sha256Digest());
        hkdf.Init(new HkdfParameters(sharedSecret, salt, Encoding.UTF8.GetBytes(info)));

        var key = new byte[KeySize];
        hkdf.GenerateBytes(key, 0, key.Length);
        return key;
    }

    public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] plaintext, byte[] associatedData)
    {
        var cipher = new ChaCha20Poly1305();
        cipher.Init(true, new AeadParameters(new(key), 128, nonce, associatedData));

        var output = new byte[cipher.GetOutputSize(plaintext.Length)];
        var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        cipher.DoFinal(output, length);
        return output;
    }

    /// <summary>Decrypts a frame, or returns <c>null</c> when the key is wrong or the frame was tampered with.</summary>
    public static byte[]? TryDecrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] associatedData)
    {
        try
        {
            var cipher = new ChaCha20Poly1305();
            cipher.Init(false, new AeadParameters(new(key), 128, nonce, associatedData));

            var output = new byte[cipher.GetOutputSize(ciphertext.Length)];
            var length = cipher.ProcessBytes(ciphertext, 0, ciphertext.Length, output, 0);
            cipher.DoFinal(output, length);
            return output;
        }
        catch (InvalidCipherTextException)
        {
            return null;
        }
    }

    /// <summary>The short code both devices show while pairing, derived from the shared transcript.</summary>
    public static string PairingCode(byte[] transcript)
    {
        var digest = new Sha256Digest();
        var hash = new byte[digest.GetDigestSize()];
        digest.BlockUpdate(transcript, 0, transcript.Length);
        digest.DoFinal(hash, 0);

        var code = Convert.ToHexString(hash, 0, 4);
        return $"{code[..4]}-{code[4..]}";
    }
}
