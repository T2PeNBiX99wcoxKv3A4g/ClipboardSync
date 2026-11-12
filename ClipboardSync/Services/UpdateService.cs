using ClipboardSync.Messaging;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;

namespace ClipboardSync.Services;

/// <summary>
///     Answers <c>app.updateStatus</c> and <c>app.checkUpdate</c> by reading the version file the
///     release workflow publishes. It is a plain text download, so checking never touches the GitHub
///     API rate limit.
/// </summary>
internal sealed class UpdateService : Singleton<UpdateService>
{
    /// <summary>Written by the release workflow and updated with every tagged build.</summary>
    private const string VersionUrl =
        "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/raw/refs/heads/master/.version";

    public const string ReleaseUrl = "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/releases/latest";

    /// <summary>Shorter than the bridge's own request timeout, so the button always gets an answer.</summary>
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    /// <summary>How long an answer is reused. The settings page asks on every open; the button forces one.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateCheckPayload? _cached;
    private DateTime _cachedAt;

    public UpdateService() => NativeBridge.MessageReceived += HandleMessageAsync;

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "app.updateStatus" => StatusAsync(context),
        "app.checkUpdate" => CheckAsync(context),
        "app.openRelease" => Task.FromResult<NativeResponse?>(OpenRelease(context)),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    private async Task<NativeResponse?> StatusAsync(NativeContext context) =>
        context.Reply("app.updateStatus.result", await ResolveAsync(false));

    private async Task<NativeResponse?> CheckAsync(NativeContext context) =>
        await ResolveAsync(true) is { } result
            ? context.Reply("app.checkUpdate.result", result)
            : context.Fail("app.checkUpdate.result", Localization.Get("error.updateCheckFailed"));

    private async Task<UpdateCheckPayload?> ResolveAsync(bool force)
    {
        await _gate.WaitAsync();
        try
        {
            if (!force && _cached is { } cached && DateTime.UtcNow - _cachedAt < CacheLifetime)
                return cached;

            var published = await Try.Run(ReadPublishedVersionAsync);

            if (published.Exception is { } exception)
            {
                Log.Warning(exception, "Failed to check for a newer version");
                return null;
            }

            if (published.Value is not { Length: > 0 } latest)
                return null;

            var current = AppInfoService.Version;
            _cachedAt = DateTime.UtcNow;
            _cached = new(current, latest, IsNewer(latest, current));

            WindowManager.Instance.NotifyMainWindow("app.update", _cached);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string> ReadPublishedVersionAsync()
    {
        using var response = await Client.GetAsync(VersionUrl);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync();
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        return line.TrimStart('v', 'V');
    }

    private static bool IsNewer(string latest, string current) =>
        Try.Run(() => Version.Parse(latest) > Version.Parse(current)).GetOrElse(exception =>
        {
            Constants.Logger.Debug(exception, "Could not compare versions {Latest} and {Current}", latest, current);
            return false;
        });

    private static NativeResponse OpenRelease(NativeContext context)
    {
        var started = Try.Run(() => SystemLauncher.Open(ReleaseUrl));

        if (started.Exception is not { } exception)
            return context.Reply("app.openRelease.result", new OkPayload());

        Constants.Logger.Warning(exception, "Failed to open the release page");
        return context.Fail("app.openRelease.result", Localization.Get("error.openReleaseFailed"));
    }
}