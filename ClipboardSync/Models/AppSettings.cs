namespace ClipboardSync.Models;

/// <summary>
///     Persisted settings. Only the options the interface already shows are here; the transport and
///     pairing model will be decided before any of them is wired to real syncing.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The name other devices will see. Defaults to this machine's name.</summary>
    public string DeviceName { get; init; } = Environment.MachineName;

    /// <summary>UI and backend language: <c>en</c>, <c>ja</c>, <c>zh-CN</c> or <c>zh-TW</c>.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Start with the window hidden in the tray.</summary>
    public bool StartHidden { get; init; }

    /// <summary>Whether this device sends and receives clipboard items.</summary>
    public bool SyncEnabled { get; init; } = true;

    /// <summary>Share text clips, including URLs and code.</summary>
    public bool SyncText { get; init; } = true;

    /// <summary>Share images copied to the clipboard.</summary>
    public bool SyncImages { get; init; } = true;

    /// <summary>Share copied files.</summary>
    public bool SyncFiles { get; init; }

    /// <summary>How many items the local history keeps.</summary>
    public int HistoryLimit { get; init; } = 200;

    /// <summary>Images larger than this many megabytes are ignored. Defaults to 10 MB.</summary>
    public int MaxImageMb { get; init; } = 10;

    public static AppSettings Default { get; } = new();
}