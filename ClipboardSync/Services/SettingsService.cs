using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Platform;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Services;

namespace ClipboardSync.Services;

/// <summary>Loads and stores the TOML settings file. The defaults apply until the user saves something.</summary>
[Singleton]
internal partial class SettingsService
{
    private const string Header =
        "# ClipboardSync settings. Rewritten when settings are saved; comments are not preserved.";

    private static readonly TomlSettingsStore<AppSettings> Store = new(
        Path.Combine(Constants.SettingsDirectory, "settings.toml"),
        AppSettings.Default,
        AppTomlContext.Default.AppSettings,
        json => JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings),
        Path.Combine(Constants.SettingsDirectory, "settings.json"),
        Header);

    private SettingsService() => NativeBridge.MessageReceived += HandleMessageAsync;

    public AppSettings Current { get; private set; } = AppSettings.Default;

    /// <summary>The language both the backend and the interface use.</summary>
    public string Language => Current.Language;

    /// <summary>Raised after a save, so the sync services can follow the new switches.</summary>
    public static event Action<SettingsPayload>? Changed;

    public async Task LoadAsync() => Current = Normalize(await Store.LoadAsync());

    private static AppSettings Normalize(AppSettings stored) => stored with
    {
        DeviceName = stored.DeviceName is { Length: > 0 } name ? name.Trim() : AppSettings.Default.DeviceName,
        Language = NormalizeLanguage(stored.Language),
        HistoryLimit = Math.Clamp(stored.HistoryLimit, 10, 5000),
        MaxImageMb = Math.Clamp(stored.MaxImageMb, ClipboardLimits.MinImageMb, ClipboardLimits.MaxImageMb),
        MaxFileMb = stored.MaxFileMb > 0
            ? Math.Clamp(stored.MaxFileMb, ClipboardLimits.MinFileMb, ClipboardLimits.MaxFileMb)
            : ClipboardLimits.DefaultFileMb,
        BlobDirectory = stored.BlobDirectory?.Trim() ?? string.Empty
    };

    private static string NormalizeLanguage(string? language) =>
        language is "system" or "en" or "ja" or "zh-CN" or "zh-TW" ? language : "system";

    private SettingsPayload Payload() =>
        new(Current.DeviceName, Current.Language, Current.StartHidden, Current.SyncEnabled,
            Current.SyncText, Current.SyncImages, Current.SyncFiles, Current.HistoryLimit, Current.MaxImageMb,
            Current.MaxFileMb, Current.BlobDirectory);

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context) => context.Request.Type switch
    {
        "settings.get" => Task.FromResult<NativeResponse?>(context.Reply("settings.get.result", Payload())),
        "settings.save" => SaveAsync(context),
        _ => Task.FromResult<NativeResponse?>(null)
    };

    private async Task<NativeResponse?> SaveAsync(NativeContext context)
    {
        if (context.Payload<AppSettings>() is not { } settings)
            return context.Fail("settings.save.result", Localization.Get("error.invalidSettings"));

        if (settings.DeviceName?.Trim() is not { Length: > 0 } deviceName)
            return context.Fail("settings.save.result", Localization.Get("error.invalidDeviceName"));

        var blobDirectory = settings.BlobDirectory?.Trim() ?? string.Empty;

        try
        {
            await BlobStore.MoveToAsync(blobDirectory);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not move the blob store to {Directory}", blobDirectory);
            return context.Fail("settings.save.result", Localization.Get("error.blobDirectory"));
        }

        Current = Normalize(settings with
        {
            DeviceName = deviceName,
            BlobDirectory = blobDirectory.Length == 0 ? string.Empty : Path.GetFullPath(blobDirectory)
        });

        Log.Information("Settings saved for {DeviceName}; sync {State}", Current.DeviceName,
            Current.SyncEnabled ? "on" : "off");

        if (!await Store.SaveAsync(Current))
            Log.Warning("The settings were not written to {File}", Store.FilePath);

        var payload = Payload();
        WindowManager.Instance.NotifyMainWindow("settings.changed", payload);
        Changed?.Invoke(payload);
        return context.Reply("settings.save.result", payload);
    }
}
