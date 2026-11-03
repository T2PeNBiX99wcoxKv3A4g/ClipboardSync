namespace ClipboardSync.Security;

/// <summary>
///     The wire contract shared by pairing and encrypted sessions. A frame is a 4-byte big-endian
///     length followed by UTF-8 JSON; the first field says which message it is. This mirrors
///     URLCheck's forwarding protocol, with ClipboardSync-specific transcript labels.
/// </summary>
internal static class PeerProtocol
{
    /// <summary>Upper bound for a control frame; a 32 MB image plus base64 framing still fits.</summary>
    public const int MaxFrame = 96 * 1024 * 1024;

    /// <summary>Port the encrypted peer listener uses.</summary>
    public const int SyncPort = 45_456;

    /// <summary>Fixed UDP port used for local-network discovery.</summary>
    public const int DiscoveryPort = 45_457;

    /// <summary>Largest text accepted from the clipboard; larger values are skipped instead of framed.</summary>
    public const int MaxClipboardText = 512 * 1024;

    public const string Discover = "discover";
    public const string Announce = "announce";
    public const string ClipboardItem = "clipboard.item";
    public const string ClipboardAck = "clipboard.ack";

    public const string PairRequest = "pair.request";
    public const string PairChallenge = "pair.challenge";
    public const string PairAccept = "pair.accept";
    public const string PairReject = "pair.reject";

    public const string SessionHello = "session.hello";
    public const string SessionReply = "session.reply";
    public const string SecureData = "secure.data";
    public const string Status = "status";

    /// <summary>How long a pairing request waits for the local user to confirm it.</summary>
    public static readonly TimeSpan PairTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long one connection may take before it is abandoned.</summary>
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);
}

/// <summary>Opens a pairing: the initiator's identity and proof that it owns its key.</summary>
internal sealed record PairRequestMessage(
    string Type,
    string DeviceId,
    string DeviceName,
    int Port,
    string PublicKey,
    string Nonce,
    string Signature);

/// <summary>Sent before the user decides, so both sides can show the same verification code.</summary>
internal sealed record PairChallengeMessage(string Type, string DeviceId, string DeviceName, int Port, string PublicKey);

/// <summary>The target agreed to pair; the signature binds it to the whole transcript.</summary>
internal sealed record PairAcceptMessage(string Type, string Signature);

internal sealed record PairRejectMessage(string Type, string Reason);

/// <summary>The pairing details the local user confirms, including the code the other side shows.</summary>
internal sealed record PairingPrompt(string DeviceId, string DeviceName, string PublicKey, string Code);

/// <summary>Opens an encrypted session: the initiator's identity plus an ephemeral X25519 key.</summary>
internal sealed record SessionHelloMessage(
    string Type,
    string DeviceId,
    string EphemeralKey,
    string Nonce,
    string Signature);

/// <summary>The receiver's ephemeral key and signature over the whole handshake.</summary>
internal sealed record SessionReplyMessage(string Type, string EphemeralKey, string Nonce, string Signature);

/// <summary>An encrypted payload. The body is opaque to the wire format.</summary>
internal sealed record SecureDataMessage(string Type, string Nonce, string Ciphertext);

/// <summary>A plain acknowledgement; failures carry a readable reason.</summary>
internal sealed record SecureStatusMessage(string Type, bool Ok, string? Error);

/// <summary>A device's reply to a discovery query, and the identity a peer pins when it pairs.</summary>
internal sealed record DiscoveryMessage(
    string Type,
    string DeviceId,
    string DeviceName,
    int Port,
    string PublicKey);

/// <summary>One clipboard item sent inside an encrypted session.</summary>
internal sealed record ClipboardItemMessage(
    string Type,
    string Id,
    string OriginDeviceId,
    string OriginDeviceName,
    string Kind,
    string Content,
    string? Meta,
    long At,
    string? Data);

/// <summary>The receiver's acknowledgement for one clipboard item.</summary>
internal sealed record ClipboardAckMessage(string Type, string Id, bool Ok, string? Error);
