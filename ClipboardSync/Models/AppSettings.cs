using PhotinoNetUtils.Settings;

namespace ClipboardSync.Models;

/// <summary>
///     Persisted settings. Only the options the interface already shows are here; the transport and
///     pairing model will be decided before any of them is wired to real syncing.
/// </summary>
[SettingsModel]
public sealed record AppSettings
{
    /// <summary>The name other devices will see. Defaults to this machine's name.</summary>
    [Setting(Comment = "The name the other devices see.")]
    public string DeviceName { get; init; } = Environment.MachineName;

    /// <summary>UI and backend language: <c>system</c>, <c>en</c>, <c>ja</c>, <c>zh-CN</c> or <c>zh-TW</c>.</summary>
    [Setting(Comment = "Interface and backend language: system, en, ja, zh-CN or zh-TW.")]
    public string Language { get; init; } = "system";

    /// <summary>Start with the window hidden in the tray.</summary>
    [Setting(Comment = "Start with the window hidden in the tray.")]
    public bool StartHidden { get; init; }

    /// <summary>Whether this device sends and receives clipboard items.</summary>
    [Setting(Comment = "Turn syncing on or off.")]
    public bool SyncEnabled { get; init; } = true;

    /// <summary>Share text clips, including URLs and code.</summary>
    [Setting(Comment = "Share copied text and links.")]
    public bool SyncText { get; init; } = true;

    /// <summary>Share images copied to the clipboard.</summary>
    [Setting(Comment = "Share copied images.")]
    public bool SyncImages { get; init; } = true;

    /// <summary>Share copied files and folders.</summary>
    [Setting(Comment = "Share copied files and folders.")]
    public bool SyncFiles { get; init; } = true;

    /// <summary>How many items the local history keeps.</summary>
    [Setting(Comment = "How many items the local history keeps.", Min = 10, Max = 5000)]
    public int HistoryLimit { get; init; } = 200;

    /// <summary>Images larger than this many megabytes are ignored. Defaults to 10 MB.</summary>
    [Setting(Comment = "Images larger than this are skipped, in megabytes.", Min = 1, Max = 32)]
    public int MaxImageMb { get; init; } = 10;

    /// <summary>Total size of one copied file selection, in megabytes. Defaults to 512 MB.</summary>
    [Setting(Comment = "One copied selection larger than this is skipped, in megabytes.", Min = 1, Max = 4096)]
    public int MaxFileMb { get; init; } = 512;

    /// <summary>Where file blobs live; empty uses the app data folder.</summary>
    [Setting(Comment = "Where file blobs live; empty uses the app data folder.")]
    public string BlobDirectory { get; init; } = string.Empty;

    public static AppSettings Default { get; } = new();
}
