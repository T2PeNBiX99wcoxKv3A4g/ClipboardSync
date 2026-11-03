namespace ClipboardSync.Messaging;

// Response bodies. These are named records rather than anonymous types because the
// source-generated JSON context has to know every shape the app can serialize.

/// <summary>Body for a bare <c>{ "ok": true }</c> acknowledgement.</summary>
internal sealed record OkPayload(bool Ok = true);

/// <summary>Body for <c>app.info</c>: the build, the platform, and where the app writes its logs.</summary>
internal sealed record AppInfoPayload(string Version, string Platform, string LogDirectory, bool Rounded, bool Debug);

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
    int MaxImageMb);

/// <summary>Payload for <c>app.setStartup</c>: whether the startup entry should exist.</summary>
internal sealed record StartupRequest(bool Enabled);

/// <summary>Body for the <c>app.startup</c> messages; the entry's presence is the state.</summary>
internal sealed record StartupPayload(bool Supported, bool Enabled);

/// <summary>Body for the <c>window.state</c> event.</summary>
internal sealed record WindowStatePayload(bool Maximized);

/// <summary>Payload for <c>window.resize</c>: the edge being dragged.</summary>
internal sealed record WindowResizeRequest(string Direction);

/// <summary>Payload for <c>app.navigate</c>: the page the tray asked the interface to open.</summary>
internal sealed record AppNavigatePayload(string Page);
