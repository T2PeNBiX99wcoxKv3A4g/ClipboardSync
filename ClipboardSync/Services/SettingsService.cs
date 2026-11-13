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
        new(Current.DeviceName, Current.Language, Current.Theme, Current.StartHidden, Current.SyncEnabled,
            Current.SyncText, Current.SyncImages, Current.SyncFiles, Current.HistoryLimit, Current.MaxImageMb,
            Current.MaxFileMb, Current.BlobDirectory);

    protected override string? Validate(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DeviceName))
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
        DeviceName = string.IsNullOrWhiteSpace(stored.DeviceName)
            ? AppSettings.Default.DeviceName
            : stored.DeviceName.Trim(),
        Language = Localization.Normalize(stored.Language),
        Theme = stored.Theme is "light" or "dark" ? stored.Theme : "system",
        MaxFileMb = stored.MaxFileMb > 0 ? stored.MaxFileMb : ClipboardLimits.DefaultFileMb,
        BlobDirectory = stored.BlobDirectory?.Trim() ?? string.Empty
    };

    protected override void OnSaved(AppSettings settings) =>
        Constants.Logger.Information("Settings saved for {DeviceName}; sync {State}", settings.DeviceName,
            settings.SyncEnabled ? "on" : "off");
}
