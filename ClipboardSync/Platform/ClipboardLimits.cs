namespace ClipboardSync.Platform;

/// <summary>Runtime limits shared by the clipboard watchers and the sync service.</summary>
internal static class ClipboardLimits
{
    public const int DefaultImageMb = 10;
    public const int MinImageMb = 1;
    public const int MaxImageMb = 32;

    /// <summary>Largest image accepted from the clipboard; updated from the settings.</summary>
    public static int MaxImageBytes { get; set; } = DefaultImageMb * 1024 * 1024;
}