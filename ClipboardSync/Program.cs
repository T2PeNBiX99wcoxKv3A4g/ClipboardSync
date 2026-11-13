using System.Diagnostics.CodeAnalysis;
using ClipboardSync.Messaging;
using ClipboardSync.Security;
using ClipboardSync.Services;
using PhotinoNetUtils;
using PhotinoNetUtils.Interop;
using PhotinoNetUtils.Localization;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Services;
using Serilog;
#if !DEBUG
using PhotinoNetUtils.Hosting;
#endif

namespace ClipboardSync;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("ClipboardSync supports Windows and Linux.");

        // Main must stay synchronous: [STAThread] is ignored for an async entry point, and the
        // Photino message loop / WebView2 require an STA thread.
        Logging.Initialize(Constants.Name, "clipboardsync");

        // The bridge serializes through this context; it has to know it before the interface speaks.
        NativeBridge.Initialize(AppJsonContext.Default);

        // Discover the translation files first: settings normalization maps a stored locale to one of them.
        Localization.Initialize(typeof(Program).Assembly, "ClipboardSync.Resources.i18n.",
            () => SettingsService.Instance.Language);

        // The language lives in the settings; load them before anything asks for a string.
        SettingsService.Instance.LoadAsync().GetAwaiter().GetResult();
        BlobStore.Configure(SettingsService.Instance.Current.BlobDirectory);

        if (OperatingSystem.IsLinux())
        {
            LinuxDiagnostics.Write();

            // The Wayland app id must match the desktop entry's file name, or the compositor will not
            // associate our windows with our icon.
            LinuxGtk.SetProgramName("clipboardsync");
        }

        try
        {
            foreach (var arg in args)
                Log.Information("arg: {Arg}", arg);

            if (args.Contains("--identity"))
            {
                var identity = DeviceIdentity.Instance;
                Console.WriteLine($"id={identity.Id}");
                Console.WriteLine($"publicKey={identity.PublicKeyBase64}");
                Console.WriteLine($"fingerprint={identity.Fingerprint}");
                Environment.Exit(0);
            }

            // Creates the long-term identity on first launch and loads the pinned peers. Neither
            // needs a window; both have to be ready before a pairing or session can start.
            _ = DeviceIdentity.Instance;
            PeerStore.Instance.InitializeAsync().GetAwaiter().GetResult();

            // Only one instance may run; later launches forward their arguments to it and exit.
            using var singleInstance = new SingleInstance(Constants.Name);
            if (!singleInstance.IsFirstInstance)
            {
                // A second launch with no arguments is still an activation request: send a marker so
                // the running instance shows its window instead of ignoring the empty payload.
                singleInstance.ForwardAsync(args.Length == 0 ? ["--activate"] : args).GetAwaiter().GetResult();
                return;
            }

#if !DEBUG
            // Kestrel binds to an OS-assigned port, so several Photino apps can run side by side.
            using var staticServer = StaticServer.Start(Constants.DataDirectory);
            var appUrl = $"{staticServer.BaseUrl}/index.html?v={AppBuild.Token}";
#else
            // A Debug build always talks to the Vite dev server, which is where hot reload lives.
            const string appUrl = "http://127.0.0.1:3300";
#endif

            Log.Information("Serving React app at {AppUrl}", appUrl);

            using var appInfo = new AppInfoService(new AppInfoOptions(
                typeof(Program).Assembly, Constants.LogDirectory, AppBuild.Debug,
                () => WindowManager.Instance.DrawsWindowCorners));
            using var startup = new StartupService(new StartupRegistration(
                typeof(Program).Assembly, Constants.Name, "edit-paste",
                "Clipboard sharing across your devices"));

            // Touching each singleton constructs it, which is what registers its message handlers.
            _ = AppLogsService.Instance;
            _ = UpdateService.Instance;

            var manager = WindowManager.Instance;
            manager.AppUrl = appUrl;
            manager.IconPath = AppIcon.EnsureExtracted(typeof(Program).Assembly, Constants.Name);

            // Without a tray the window is the only way back in, so it always shows there.
            var hasTray = OperatingSystem.IsWindows() ||
                          OperatingSystem.IsLinux() && LinuxDiagnostics.AppIndicatorLibrary is not null;
            manager.ShowMainWindowOnStartup = !SettingsService.Instance.Current.StartHidden || !hasTray;

            // Touching these starts the clipboard watcher, peer listener and discovery. They queue
            // their first events until the interface reports in.
            _ = PairingService.Instance;
            ClipboardStore.Instance.InitializeAsync().GetAwaiter().GetResult();
            _ = SyncService.Instance;

            using var tray = CreateTray(manager.IconPath);

            manager.MainWindowCreated +=
                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                () =>
                    singleInstance.StartServer(_ => manager.ShowMainWindow());

            manager.CreateMainWindow();
            StartTray(tray);

            using var termination = TerminationSignals.Handle(manager.Exit);
            manager.Run();

            Log.Information("Exiting");
        }
        catch (Exception ex)
        {
            // The window procedure runs native code, so a failure there surfaces here. Record it
            // before the runtime prints its own crash dialog.
            Log.Fatal(ex, "Unhandled exception");
            throw;
        }
        finally
        {
            Logging.Shutdown();
        }
    }

    /// <summary>
    ///     Starts the tray icon and records whether it exists, so closing the window only hides it
    ///     when there is really something to restore it from.
    /// </summary>
    private static void StartTray(ITrayIcon tray)
    {
        var manager = WindowManager.Instance;
        TrayIconFactory.StartWhenReady(tray, manager.RunOnMainWindow, started => manager.CloseToTray = started);
    }

    /// <summary>The platform's tray icon, or a no-op stand-in when there is none.</summary>
    private static ITrayIcon CreateTray(string? iconPath)
    {
        var manager = WindowManager.Instance;
        var appId = OperatingSystem.IsLinux() ? "clipboardsync" : Constants.Name;

        return TrayIconFactory.CreateStandard(
            appId,
            Constants.WindowTitle,
            iconPath,
            Localization.Get("tray.open"),
            Localization.Get("tray.settings"),
            Localization.Get("tray.exit"),
            () => manager.ShowMainWindow(),
            () => manager.ShowMainWindow("settings"),
            manager.Exit,
            manager.ReloadWebView,
            Localization.Get("tray.reload"));
    }
}