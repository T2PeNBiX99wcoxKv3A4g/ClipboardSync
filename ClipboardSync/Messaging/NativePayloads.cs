namespace ClipboardSync.Messaging;

// Response bodies. These are named records rather than anonymous types because the
// source-generated JSON context has to know every shape the app can serialize.

/// <summary>Body for a bare <c>{ "ok": true }</c> acknowledgement.</summary>
internal sealed record OkPayload(bool Ok = true);

/// <summary>Body for <c>settings.get</c> and for the <c>settings.changed</c> event.</summary>
internal sealed record SettingsPayload(
    string DeviceName,
    string Language,
    bool StartHidden,
    bool SyncEnabled,
    bool SyncText,
    bool SyncImages,
    bool SyncFiles,
    int HistoryLimit,
    int MaxImageMb,
    int MaxFileMb,
    string BlobDirectory);

/// <summary>Body for <c>app.updateStatus</c> and <c>app.checkUpdate</c>.</summary>
internal sealed record UpdateCheckPayload(string Current, string Latest, bool Available);

/// <summary>Payload for <c>app.navigate</c>: the page the tray asked the interface to open.</summary>
internal sealed record AppNavigatePayload(string Page);
