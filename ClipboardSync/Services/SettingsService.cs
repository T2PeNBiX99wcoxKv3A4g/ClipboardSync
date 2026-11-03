using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Platform;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>Loads and stores the settings file. The defaults apply until the user saves something.</summary>
internal sealed class SettingsService : Singleton<SettingsService>
{
    private static readonly string FilePath = Path.Combine(Constants.SettingsDirectory, "settings.json");

    public SettingsService() => NativeBridge.MessageReceived += HandleMessageAsync;

    /// <summary>Raised after a save, so the sync services can follow the new switches.</summary>
    public static event Action<SettingsPayload>? Changed;

    public AppSettings Current { get; private set; } = AppSettings.Default;

    /// <summary>The language both the backend and the interface use.</summary>
    public string Language => Current.Language;

    public void Load()
    {
        if (!File.Exists(FilePath))
            return;

        var stored = Try.Run(() => JsonSerializer.Deserialize(File.ReadAllText(FilePath),
                AppJsonContext.Default.AppSettings))
            .OnFailure(ex => Log.Warning(ex, "Failed to read {File}", FilePath))
            .GetOrNull();

        if (stored is not null)
            Current = stored with
            {
                DeviceName = stored.DeviceName is { Length: > 0 } name ? name.Trim() : Current.DeviceName,
                Language = NormalizeLanguage(stored.Language),
                HistoryLimit = Math.Clamp(stored.HistoryLimit, 10, 5000),
                MaxImageMb = Math.Clamp(stored.MaxImageMb, ClipboardLimits.MinImageMb, ClipboardLimits.MaxImageMb)
            };
    }

    private static string NormalizeLanguage(string? language) =>
        language is "ja" or "zh-CN" or "zh-TW" ? language : "en";

    private SettingsPayload Payload() =>
        new(Current.DeviceName, Current.Language, Current.StartHidden, Current.SyncEnabled,
            Current.SyncText, Current.SyncImages, Current.SyncFiles, Current.HistoryLimit, Current.MaxImageMb);

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

        if (settings.DeviceName.Trim() is not { Length: > 0 } deviceName)
            return context.Fail("settings.save.result", Localization.Get("error.invalidDeviceName"));

        Current = new()
        {
            DeviceName = deviceName,
            Language = NormalizeLanguage(settings.Language),
            StartHidden = settings.StartHidden,
            SyncEnabled = settings.SyncEnabled,
            SyncText = settings.SyncText,
            SyncImages = settings.SyncImages,
            SyncFiles = settings.SyncFiles,
            HistoryLimit = Math.Clamp(settings.HistoryLimit, 10, 5000),
            MaxImageMb = Math.Clamp(settings.MaxImageMb, ClipboardLimits.MinImageMb, ClipboardLimits.MaxImageMb)
        };

        Log.Information("Settings saved for {DeviceName}; sync {State}", Current.DeviceName,
            Current.SyncEnabled ? "on" : "off");

        var attempt = await Try.Run(() => File.WriteAllTextAsync(FilePath,
            JsonSerializer.Serialize(Current, AppJsonContext.Default.AppSettings)));

        attempt.OnFailure(ex => Log.Warning(ex, "Failed to write {File}", FilePath));

        var payload = Payload();
        WindowManager.Instance.NotifyMainWindow("settings.changed", payload);
        Changed?.Invoke(payload);
        return context.Reply("settings.save.result", payload);
    }
}
