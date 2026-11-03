using ClipboardSync.Messaging;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     Registers ClipboardSync to start with the user's session. The autostart entry's presence is
///     the state, so deleting it by hand turns the feature off too.
/// </summary>
/// <remarks>
///     Windows uses a shortcut in the per-user Startup folder; Linux uses the XDG autostart directory.
/// </remarks>
internal sealed class StartupService : Singleton<StartupService>
{
    private readonly StartupRegistration _registration =
        new(typeof(StartupService).Assembly, Constants.Name, "edit-paste",
            "Clipboard sharing across your devices");

    public StartupService()
    {
        NativeBridge.MessageReceived += HandleMessageAsync;
        Refresh();
    }

    public static bool IsSupported => StartupRegistration.IsSupported;

    private bool IsEnabled => _registration.IsEnabled;

    /// <summary>Rewrites an existing entry so a moved executable or a new icon is picked up.</summary>
    private void Refresh()
    {
        if (IsEnabled)
            Try.Run(_registration.Refresh).OnFailure(ex => Log.Warning(ex, "Failed to refresh the autostart entry"));
    }

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "app.startup" => Task.FromResult<NativeResponse?>(Status(context)),
        "app.setStartup" => Task.FromResult<NativeResponse?>(Set(context)),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    private NativeResponse Status(NativeContext context) =>
        context.Reply("app.startup.result", new StartupPayload(IsSupported, IsEnabled));

    private NativeResponse Set(NativeContext context)
    {
        var enabled = context.Payload<StartupRequest>()?.Enabled ?? false;

        if (!IsSupported)
            return context.Reply("app.setStartup.result", new StartupPayload(false, false));

        Try.Run(() => _registration.SetEnabled(enabled))
            .OnFailure(ex => Log.Warning(ex, "Failed to update the autostart entry"));

        return context.Reply("app.setStartup.result", new StartupPayload(IsSupported, IsEnabled));
    }
}