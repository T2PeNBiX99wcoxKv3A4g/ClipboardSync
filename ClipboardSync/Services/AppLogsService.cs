using ClipboardSync.Messaging;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>Opens the log folder, so a bug report can include the file next to it.</summary>
[Singleton]
internal partial class AppLogsService
{
    [NativeMessage("app.openLogs")]
    private static NativeResponse Open(NativeContext context)
    {
        var started = Try.Run(() => SystemLauncher.Open(Constants.LogDirectory));

        if (started.Exception is not { } exception)
            return context.Reply("app.openLogs.result", new OkPayload());

        Constants.Logger.Warning(exception, "Failed to open {Directory}", Constants.LogDirectory);
        return context.Fail("app.openLogs.result", Localization.Get("error.openLogsFailed"));
    }
}
