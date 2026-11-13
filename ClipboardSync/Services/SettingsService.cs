using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Platform;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Services;

namespace ClipboardSync.Services;

/// <summary>Owns the settings model and its TOML file; the shared base handles the standard messages.</summary>
internal sealed class SettingsService() : SettingsServiceBase<SettingsService, AppSettings, SettingsPayload>(
    new TomlSettingsStore<AppSettings>(
        Path.Combine(Constants.SettingsDirectory, "settings.toml"),
        AppSettings.Default,
        AppTomlContext.Default.AppSettings,
        json => JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings),
        Path.Combine(Constants.SettingsDirectory, "settings.json"),
        Header),
    (type, data) => WindowManager.Instance.NotifyMainWindow(type, data))
{
    private const string Header =
        "# ClipboardSync settings. Rewritten when settings are saved; comments are not preserved.";

    /// <summary>The language both the backend and the interface use.</summary>
    public string Language => Current.Language;

    protected override SettingsPayload Payload() =>
        new(Current.DeviceName, Current.Language, Current.StartHidden, Current.SyncEnabled,
            Current.SyncText, Current.SyncImages, Current.SyncFiles, Current.HistoryLimit, Current.MaxImageMb,
            Current.MaxFileMb, Current.BlobDirectory);

    protected override string? Validate(AppSettings settings)
    {
        if (settings.DeviceName?.Trim() is not { Length: > 0 })
            return Localization.Get("error.invalidDeviceName");

        try
        {
            BlobStore.MoveToAsync(settings.BlobDirectory?.Trim() ?? string.Empty).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Could not move the blob store to {Directory}", settings.BlobDirectory);
            return Localization.Get("error.blobDirectory");
        }

        return null;
    }

    protected override AppSettings Merge(AppSettings settings) => settings with
    {
        BlobDirectory = settings.BlobDirectory is { Length: > 0 } directory
            ? Path.GetFullPath(directory.Trim())
            : string.Empty
    };

    protected override AppSettings Normalize(AppSettings stored) => stored with
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

    protected override void OnSaved(AppSettings settings) =>
        Constants.Logger.Information("Settings saved for {DeviceName}; sync {State}", settings.DeviceName,
            settings.SyncEnabled ? "on" : "off");

    private static string NormalizeLanguage(string? language) =>
        language is "system" or "en" or "ja" or "zh-CN" or "zh-TW" ? language : "system";
}
