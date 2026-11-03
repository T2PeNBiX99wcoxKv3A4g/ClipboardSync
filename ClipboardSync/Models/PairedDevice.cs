namespace ClipboardSync.Models;

/// <summary>
///     A device that has been paired with this one. The Ed25519 public key is pinned when the user
///     confirms the pairing; every later handshake is verified against it.
/// </summary>
public sealed class PairedDevice
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Host name or IP address of the peer on the local network.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Port the peer's listener is bound to.</summary>
    public int Port { get; set; }

    /// <summary>The peer's Ed25519 public key, base64. Empty until the device has been paired.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Extra addresses tried when <see cref="Host" /> cannot be reached.</summary>
    public List<string> Addresses { get; set; } = [];

    /// <summary>The primary <see cref="Host" /> followed by every extra address.</summary>
    public IEnumerable<string> AllAddresses()
    {
        if (!string.IsNullOrWhiteSpace(Host))
            yield return Host.Trim();

        foreach (var address in Addresses)
            if (!string.IsNullOrWhiteSpace(address))
                yield return address.Trim();
    }
}