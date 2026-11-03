using ClipboardSync.Messaging;
using JetBrains.Annotations;
using PhotinoNetUtils;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>Answers the <c>app.*</c> messages about the build and its log folder.</summary>
internal sealed class AppInfoService : Singleton<AppInfoService>
{
    /// <summary>The version declared in the csproj (<c>&lt;Version&gt;</c>).</summary>
    [PublicAPI] public static readonly string Version =
        BuildInfo.VersionOf(typeof(AppInfoService).Assembly);

    /// <summary>Lower-case platform id, so the UI can adapt platform-specific actions.</summary>
    [PublicAPI] public static readonly string Platform = BuildInfo.Platform;

    /// <summary>Whether this is a debug build, which is the only one that shows the debug page.</summary>
#if DEBUG
    [PublicAPI] public static readonly bool Debug = true;
#else
    [PublicAPI] public static readonly bool Debug = false;
#endif

    /// <summary>
    ///     Changes whenever this build does. The interface is always served from the same address, so
    ///     this is what tells the web view that what it cached belongs to an older build.
    /// </summary>
    [PublicAPI] public static readonly string BuildToken = BuildInfo.BuildTokenFor(typeof(AppInfoService).Assembly);

    public AppInfoService() => NativeBridge.MessageReceived += HandleMessageAsync;

    private static Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "app.info" => Task.FromResult<NativeResponse?>(context.Reply("app.info.result",
            new AppInfoPayload(Version, Platform, Constants.LogDirectory, WindowManager.Instance.DrawsWindowCorners,
                Debug))),
        "app.openLogs" => Task.FromResult<NativeResponse?>(OpenLogs(context)),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    /// <summary>Opens the log folder, so a bug report can include the file next to it.</summary>
    private static NativeResponse OpenLogs(NativeContext context)
    {
        var started = Try.Run(() => SystemLauncher.Open(Constants.LogDirectory));

        if (started.Exception is not { } exception)
            return context.Reply("app.openLogs.result", new OkPayload());

        Constants.Logger.Warning(exception, "Failed to open {Directory}", Constants.LogDirectory);
        return context.Fail("app.openLogs.result", Localization.Get("error.openLogsFailed"));
    }
}
