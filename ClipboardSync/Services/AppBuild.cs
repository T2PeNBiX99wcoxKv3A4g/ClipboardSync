using PhotinoNetUtils;

namespace ClipboardSync.Services;

/// <summary>Build metadata the app services and the web view cache share.</summary>
internal static class AppBuild
{
    /// <summary>The version declared in the csproj (<c>&lt;Version&gt;</c>).</summary>
    public static readonly string Version = BuildInfo.VersionOf(typeof(AppBuild).Assembly);

    /// <summary>Lower-case platform id, so the UI can adapt platform-specific actions.</summary>
    public static readonly string Platform = BuildInfo.Platform;

    /// <summary>
    ///     Changes whenever this build does. The interface is always served from the same address, so
    ///     this is what tells the web view that what it cached belongs to an older build.
    /// </summary>
    public static readonly string Token = BuildInfo.BuildTokenFor(typeof(AppBuild).Assembly);

#if DEBUG
    /// <summary>Whether this is a debug build, which is the only one that shows the debug page.</summary>
    public const bool Debug = true;
#else
    /// <summary>Whether this is a debug build, which is the only one that shows the debug page.</summary>
    public const bool Debug = false;
#endif
}
