using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Security;

/// <summary>
///     This device's long-term identity: a stable device id and an Ed25519 key pair. Peers pin the
///     public key when they pair, and every handshake is signed with the private key. Both files live
///     beside the settings so replacing the settings never exposes the key.
/// </summary>
internal sealed class DeviceIdentity : Singleton<DeviceIdentity>
{
    private const string KeyFileName = "device.key";
    private const string IdFileName = "device.id";

    public DeviceIdentity()
    {
        var directory = Constants.SettingsDirectory;
        var keyPath = Path.Combine(directory, KeyFileName);
        var idPath = Path.Combine(directory, IdFileName);

        var privateKey = Try.Run(() => ReadKey(keyPath)).GetOrNull();
        if (privateKey is null)
        {
            if (File.Exists(keyPath))
                Log.Warning("The device key at {Path} is unusable; generating a new one", keyPath);

            privateKey = ClipboardCrypto.RandomBytes(ClipboardCrypto.KeySize);
            Try.Run(() => File.WriteAllText(keyPath, ClipboardCrypto.ToBase64(privateKey)))
                .OnFailure(ex => Log.Warning(ex, "Failed to save the device key at {Path}", keyPath));
        }

        PrivateKey = privateKey;
        PublicKey = new Ed25519PrivateKeyParameters(privateKey, 0).GeneratePublicKey().GetEncoded();
        Id = ReadId(idPath);

        Log.Information("Device identity {Id} ({Fingerprint})", Id, Fingerprint);
    }

    /// <summary>Stable id shared with peers; independent of the machine name.</summary>
    public string Id { get; }

    public byte[] PrivateKey { get; }

    public byte[] PublicKey { get; }

    public string PublicKeyBase64 => ClipboardCrypto.ToBase64(PublicKey);

    /// <summary>A short, human-readable digest of the public key for a pairing screen.</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(PublicKey))[..16];

    private static byte[]? ReadKey(string path)
    {
        if (!File.Exists(path))
            return null;

        return Try.Run(() => ClipboardCrypto.FromBase64(File.ReadAllText(path)))
            .GetOrElse(ex =>
            {
                Constants.Logger.Warning(ex, "Failed to read the device key at {Path}", path);
                return null;
            }) is { Length: ClipboardCrypto.KeySize } key
            ? key
            : null;
    }

    private string ReadId(string path)
    {
        if (File.Exists(path) &&
            Try.Run(() => File.ReadAllText(path).Trim()).GetOrNull() is { Length: > 0 } stored)
            return stored;

        var id = Guid.NewGuid().ToString("N");
        Try.Run(() => File.WriteAllText(path, id))
            .OnFailure(ex => Log.Warning(ex, "Failed to save the device id at {Path}", path));

        return id;
    }
}