namespace ClipboardSync.Platform;

/// <summary>
///     Reports every new text the user copies, and can put a received text back on the clipboard.
///     Implementations must never block the clipboard for long.
/// </summary>
internal interface IClipboardWatcher : IDisposable
{
    /// <summary>Raised with the freshly copied text.</summary>
    event Action<string>? TextCopied;

    /// <summary>Raised with the PNG bytes of a freshly copied image.</summary>
    event Action<byte[]>? ImageCopied;

    void Start();

    void Stop();

    /// <summary>Reads the clipboard's text once, or null when it cannot be read right now.</summary>
    string? ReadText();

    /// <summary>Replaces the clipboard with <paramref name="text" />.</summary>
    bool WriteText(string text);

    /// <summary>Replaces the clipboard with a PNG image.</summary>
    bool WriteImage(byte[] png);
}
