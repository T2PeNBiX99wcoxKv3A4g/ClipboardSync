namespace ClipboardSync.Platform;

/// <summary>Picks the platform clipboard implementation and exposes it as one service.</summary>
internal sealed class ClipboardService : Singleton<ClipboardService>
{
    private readonly IClipboardWatcher _watcher;

    public ClipboardService()
    {
        if (OperatingSystem.IsWindows())
            _watcher = WindowsClipboardWatcher.Instance;
        else if (OperatingSystem.IsLinux())
            _watcher = LinuxClipboardWatcher.Instance;
        else
            throw new PlatformNotSupportedException("ClipboardSync supports Windows and Linux.");

        _watcher.TextCopied += text => TextCopied?.Invoke(text);
        _watcher.ImageCopied += image => ImageCopied?.Invoke(image);
        _watcher.FilesCopied += files => FilesCopied?.Invoke(files);
    }

    public event Action<string>? TextCopied;

    public event Action<byte[]>? ImageCopied;

    public event Action<string[]>? FilesCopied;

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    public string? ReadText() => _watcher.ReadText();

    public bool WriteText(string text) => _watcher.WriteText(text);

    public bool WriteImage(byte[] png) => _watcher.WriteImage(png);

    public bool WriteFiles(IReadOnlyList<string> paths) => _watcher.WriteFiles(paths);
}