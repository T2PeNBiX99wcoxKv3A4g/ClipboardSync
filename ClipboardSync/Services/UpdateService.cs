using System.Diagnostics;
using ClipboardSync.Messaging;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;

namespace ClipboardSync.Services;

/// <summary>
///     Answers <c>app.updateStatus</c>, <c>app.checkUpdate</c> and <c>app.changelog</c> from the
///     version file and changelog the release workflow publishes. On Windows it also downloads the
///     MSI and hands it to the installer.
/// </summary>
[Singleton]
internal partial class UpdateService
{
    /// <summary>Written by the release workflow and updated with every tagged build.</summary>
    private const string VersionUrl =
        "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/raw/refs/heads/master/.version";

    private const string DownloadBase =
        "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/releases/download";

    private const string ChangelogBase =
        "https://raw.githubusercontent.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync";

    public const string ReleaseUrl = "https://github.com/T2PeNBiX99wcoxKv3A4g/ClipboardSync/releases/latest";

    /// <summary>Shorter than the bridge's own request timeout, so the button always gets an answer.</summary>
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    /// <summary>Large downloads and the changelog use a timeout that a slow connection cannot trip.</summary>
    private static readonly HttpClient DownloadClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    /// <summary>How long an answer is reused. The settings page asks on every open; the button forces one.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UpdateCheckPayload? _cached;
    private DateTime _cachedAt;
    private string? _changelog;
    private string? _changelogVersion;
    private DateTime _changelogAt;
    private long _lastProgressAt;

    private UpdateService() => NativeBridge.MessageReceived += HandleMessageAsync;

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "app.updateStatus" => StatusAsync(context),
        "app.checkUpdate" => CheckAsync(context),
        "app.changelog" => ChangelogAsync(context),
        "app.downloadUpdate" => Task.FromResult<NativeResponse?>(StartDownload(context)),
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

            var current = AppBuild.Version;
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

    private async Task<NativeResponse?> ChangelogAsync(NativeContext context)
    {
        var update = await ResolveAsync(false);
        var version = update?.Latest;

        if (_changelog is { } cached && _changelogVersion == version &&
            DateTime.UtcNow - _changelogAt < CacheLifetime)
            return context.Reply("app.changelog.result", new ChangelogPayload(cached));

        var masterUrl = $"{ChangelogBase}/refs/heads/master/CHANGELOG.md";
        var result = version is { Length: > 0 }
            ? await Try.Run(() => ReadChangelogAsync($"{ChangelogBase}/v{version}/CHANGELOG.md"))
            : await Try.Run(() => ReadChangelogAsync(masterUrl));

        // A release tag without the file still has the current changelog on master.
        if (result.Exception is not null && version is { Length: > 0 })
            result = await Try.Run(() => ReadChangelogAsync(masterUrl));

        if (result.Exception is { } exception)
        {
            Constants.Logger.Warning(exception, "Failed to read the changelog");
            return context.Fail("app.changelog.result", Localization.Get("error.changelogFailed"));
        }

        _changelog = result.Value ?? string.Empty;
        _changelogVersion = version;
        _changelogAt = DateTime.UtcNow;
        return context.Reply("app.changelog.result", new ChangelogPayload(_changelog));
    }

    private static async Task<string> ReadChangelogAsync(string url)
    {
        using var response = await DownloadClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private NativeResponse StartDownload(NativeContext context)
    {
        if (!OperatingSystem.IsWindows())
            return context.Fail("app.downloadUpdate.result", Localization.Get("error.updateWindowsOnly"));

        if (!_downloadGate.Wait(0))
            return context.Fail("app.downloadUpdate.result", Localization.Get("error.updateInProgress"));

        _ = Task.Run(async () =>
        {
            try
            {
                await DownloadAndInstallAsync();
            }
            finally
            {
                _downloadGate.Release();
            }
        });

        return context.Reply("app.downloadUpdate.result", new OkPayload());
    }

    private async Task DownloadAndInstallAsync()
    {
        long received = 0;
        long total = 0;

        try
        {
            var update = await ResolveAsync(true);
            if (update is not { Available: true } || !Version.TryParse(update.Latest, out _))
            {
                NotifyProgress(0, 0, false, false, Localization.Get("error.updateDownloadFailed"));
                return;
            }

            var version = update.Latest;
            var fileName = $"ClipboardSync-{version}-win-x64.msi";
            var directory = Path.Combine(Path.GetTempPath(), "ClipboardSync", "updates");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, fileName);

            if (File.Exists(target))
                File.Delete(target);

            NotifyProgress(0, 0, false, false);

            using var response = await DownloadClient.GetAsync(
                $"{DownloadBase}/v{version}/{fileName}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            total = response.Content.Headers.ContentLength ?? 0;

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int read;

                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    received += read;

                    if (Environment.TickCount64 - _lastProgressAt < 150)
                        continue;

                    _lastProgressAt = Environment.TickCount64;
                    NotifyProgress(received, total, false, false);
                }
            }

            if (total > 0 && received != total)
                throw new IOException($"Downloaded {received} of {total} bytes.");

            NotifyProgress(received, total, true, false);
            await Task.Delay(600);
            NotifyProgress(received, total, true, true);

            var command = $"ping 127.0.0.1 -n 3 >nul & msiexec.exe /i \"{target}\" /passive /norestart";
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("Could not start the installer.");

            // Let the installer start after this process releases its files.
            await Task.Delay(800);
            WindowManager.Instance.Exit();
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Failed to download or start the update");
            NotifyProgress(received, total, false, false, Localization.Get("error.updateDownloadFailed"));
        }
    }

    private static void NotifyProgress(long received, long total, bool done, bool installing, string? error = null) =>
        WindowManager.Instance.NotifyMainWindow("app.updateProgress",
            new UpdateProgressPayload(received, total, done, installing, error));

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
