using ClipboardSync.Messaging;
using Photino.NET;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Interop;
using PhotinoNetUtils.Messaging;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Services;

/// <summary>
///     Owns the Photino window and the message loop: creation, hiding to the tray and bringing it
///     back.
/// </summary>
internal sealed class WindowManager : Singleton<WindowManager>
{
    /// <summary>
    ///     Per-app web view data folder. On Windows, Photino defaults to a WebView2 folder shared by
    ///     every Photino app, which breaks when another app holds it, so keep ours separate.
    /// </summary>
    private static readonly string WebViewDataPath = Path.Combine(Constants.DataDirectory, "webview");

    private readonly Lock _gate = new();

    /// <summary>The newest event of each type, held until the page reports in.</summary>
    private readonly Dictionary<string, string> _pending = new();

    private bool _allowClose;
    private bool _hideWhenRendered;
    private PhotinoWindow? _mainWindow;
    private PhotinoNetUtils.Services.WebViewRecovery? _webViewRecovery;

    /// <summary>Whether the interface has reported in; before that, pushing an event to it crashes.</summary>
    private bool _mainWindowReady;

    public WindowManager() => NativeBridge.MessageReceived += HandleMessageAsync;

    /// <summary>
    ///     Whether the interface has to paint the window's rounded corners, because the platform leaves
    ///     an undecorated window without a frame (Linux).
    /// </summary>
    public bool DrawsWindowCorners { get; private set; }

    /// <summary>Address the window loads the interface from, set before the window is created.</summary>
    public string AppUrl { get; set; } = "";

    /// <summary>Whether the window is shown at startup instead of hiding to the tray.</summary>
    public bool ShowMainWindowOnStartup { get; set; } = true;

    /// <summary>Path to the extracted icon for the window, or null to keep the default.</summary>
    public string? IconPath { get; set; }

    /// <summary>
    ///     Whether closing the window hides it. Set once the tray reports whether it was really
    ///     created, so a missing tray can never leave the window hidden with no way back.
    /// </summary>
    public bool CloseToTray { get; set; }

    /// <summary>Raised once the window's native handle exists (the message loop is about to start).</summary>
    public event Action? MainWindowCreated;

    public void CreateMainWindow()
    {
        PrepareWebViewData();

        var window = new PhotinoWindow()
            // The native layer prints every property setter and message it handles. That is useful
            // while debugging, but it floods the log with JSON payloads in a release build.
            .SetLogVerbosity(Constants.IsDebugMode ? 3 : 0)
            .SetTitle(Constants.WindowTitle)
            .SetTemporaryFilesPath(WebViewDataPath)
            .SetUseOsDefaultSize(false)
            .SetSize(new(1180, 780))
            .SetMinSize(900, 600)
            // Only here so nothing flashes; the window is hidden once the interface has rendered.
            .SetMinimized(!ShowMainWindowOnStartup)
            .SetContextMenuEnabled(false)
            .RegisterWebMessageReceivedHandler((sender, message) =>
                _ = NativeBridge.HandleMessageAsync(sender as PhotinoWindow, message))
            .RegisterWindowClosingHandler((_, _) =>
            {
                // Stay resident: closing the window hides it instead of shutting down.
                if (_allowClose || !CloseToTray)
                {
                    Log.Information("Main window closing (allowed)");
                    return false;
                }

                Log.Information("Main window closing -> hiding to the tray");
                HideMainWindow();
                return true;
            });

        if (IconPath is { Length: > 0 } icon)
            window.SetIconFile(icon);

        // Windows keeps its native frame and only drops the non-client area, because the interface
        // draws the title bar. On Linux the GTK client-side frame is replaced by the interface's own
        // title bar; this is the same path AntiBot/URLCheck use.
        window.RegisterWindowCreatedHandler((_, _) =>
        {
            if (OperatingSystem.IsWindows())
            {
                WindowFrame.RemoveTitleBar(window.WindowHandle);

                // Photino drops a Start Menu shortcut for the toast registration and never revisits
                // it, so a build that moved or changed its icon would keep the old one.
                StartMenuShortcut.Refresh(Constants.WindowTitle);
            }
            else if (OperatingSystem.IsLinux())
            {
                LinuxWindow.UseClientSideFrame(window.Title);
                DrawsWindowCorners = LinuxWindow.EnableTransparency(window.Title);
            }

            // Hiding a window while its web view is still starting leaves it blank, so it stays
            // minimised until the interface reports in (or the timeout below hides it anyway).
            if (!ShowMainWindowOnStartup)
            {
                _hideWhenRendered = true;
                Log.Information("Starting hidden; the window stays minimised until its interface has rendered");
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => HideWhenRendered());
            }

            MainWindowCreated?.Invoke();
        });

        _webViewRecovery = PhotinoNetUtils.Services.WebViewRecovery.Attach(window, AppUrl);
        window.Load(AppUrl);
        _mainWindow = window;
    }

    /// <summary>Starts the message loop; blocks until the window is closed for good.</summary>
    public void Run() => _mainWindow?.WaitForClose();

    /// <summary>Shows the main window, restoring it first when it is hidden or minimised.</summary>
    public void ShowMainWindow(string? page = null)
    {
        Log.Information("Showing the main window");
        RunOnMainWindow(() => Show(_mainWindow!));

        if (page is { Length: > 0 })
            NotifyMainWindow("app.navigate", new AppNavigatePayload(page));
    }

    public void HideMainWindow()
    {
        Log.Information("Hiding the main window");
        RunOnMainWindow(() => Hide(_mainWindow!));
    }

    /// <summary>Runs an action on the UI thread, once the message loop is running.</summary>
    public void RunOnMainWindow(Action action) => _mainWindow?.Invoke(action);

    /// <summary>
    ///     Pushes an event to the interface. Anything sent while the web view is still loading would
    ///     abort the process, so the newest event of each type waits and is sent once the page is ready.
    /// </summary>
    public void NotifyMainWindow(string type, object? data)
    {
        var message = Try.Run(() => NativeBridge.SerializeEvent(type, data)).GetOrNull();
        if (message is null)
            return;

        if (!_mainWindowReady)
        {
            // A restore can finish before the page reports in; dropping the event would leave the
            // header showing the state from before it.
            lock (_gate)
                _pending[type] = message;

            return;
        }

        Send(message, type);
    }

    private void Send(string message, string type) =>
        Try.Run(() => _mainWindow?.SendWebMessage(message))
            .OnFailure(ex => Log.Warning(ex, "Failed to push {Type} to the interface", type));

    private void HideWhenRendered()
    {
        if (!_hideWhenRendered || _mainWindow is not { } window)
            return;

        _hideWhenRendered = false;
        RunOnMainWindow(() => Hide(window));
    }

    private void FlushPending()
    {
        KeyValuePair<string, string>[] pending;

        lock (_gate)
        {
            pending = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var (type, message) in pending)
            Send(message, type);
    }

    private Task<NativeResponse?> HandleMessageAsync(NativeContext context)
    {
        switch (context.Request.Type)
        {
            case "app.ready":
                _mainWindowReady = true;
                FlushPending();
                HideWhenRendered();
                break;

            case "window.minimize":
                RunOnMainWindow(() => _mainWindow?.SetMinimized(true));
                break;

            case "window.toggleMaximize":
                RunOnMainWindow(ToggleMaximize);
                break;

            case "window.close":
                RunOnMainWindow(() => _mainWindow?.Close());
                break;

            case "window.drag":
                RunOnMainWindow(() =>
                {
                    if (_mainWindow is not { } window)
                        return;

                    if (OperatingSystem.IsWindows())
                        WindowDrag.Begin(window.WindowHandle);
                    else if (OperatingSystem.IsLinux())
                        LinuxWindow.BeginDrag(window.Title);
                });
                break;

            case "window.resize":
                RunOnMainWindow(() =>
                {
                    if (_mainWindow is not { } window)
                        return;

                    var direction = context.Payload<WindowResizeRequest>()?.Direction ?? "";

                    if (OperatingSystem.IsWindows())
                        WindowResize.Begin(window.WindowHandle, direction);
                    else if (OperatingSystem.IsLinux())
                        LinuxWindow.BeginResize(window.Title, direction);
                });
                break;
        }

        return Task.FromResult<NativeResponse?>(null);
    }

    private void NotifyWindowState(bool maximized) =>
        NotifyMainWindow("window.state", new WindowStatePayload(maximized));

    private void ToggleMaximize()
    {
        if (_mainWindow is not { } window)
            return;

        if (OperatingSystem.IsWindows())
        {
            if (window.WindowHandle != IntPtr.Zero)
                NotifyWindowState(WindowZoom.Toggle(window.WindowHandle));

            return;
        }

        if (OperatingSystem.IsLinux())
        {
            NotifyWindowState(LinuxWindow.ToggleMaximize(window.Title));
            return;
        }

        var maximized = !window.Maximized;
        window.SetMaximized(maximized);
        NotifyWindowState(maximized);
    }

    public void Exit()
    {
        _allowClose = true;
        _webViewRecovery?.Dispose();
        RunOnMainWindow(() => _mainWindow?.Close());
    }

    /// <summary>Reloads the main UI from the tray's recovery action.</summary>
    public void ReloadWebView() => _webViewRecovery?.Reload();

    private static void Show(PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = window.WindowHandle;
            if (handle == IntPtr.Zero)
                return;

            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
            NativeMethods.SetForegroundWindow(handle);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            if (LinuxGtk.Present(window.Title))
                return;

            window.SetMinimized(false);
            window.SetTopMost(true);
        }
    }

    private static void Hide(PhotinoWindow window)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = window.WindowHandle;
            if (handle != IntPtr.Zero)
                NativeMethods.ShowWindow(handle, NativeMethods.SwHide);
            return;
        }

        // Minimising leaves the window in the taskbar; hiding it outright does not.
        if (OperatingSystem.IsLinux() && !LinuxGtk.Hide(window.Title))
            window.SetMinimized(true);
    }

    /// <summary>
    ///     Drops the web view's data folder when this build is not the one that filled it, so an
    ///     upgrade never keeps serving the previous interface from the web view cache.
    /// </summary>
    private void PrepareWebViewData()
    {
        var marker = Path.Combine(Path.GetDirectoryName(WebViewDataPath)!, "webview.build");

        Try.Run(() =>
        {
            if (File.Exists(marker) && File.ReadAllText(marker) != AppInfoService.BuildToken)
            {
                Log.Information("This is a different build; clearing the web view's cache");
                if (Directory.Exists(WebViewDataPath))
                    Directory.Delete(WebViewDataPath, true);
            }

            File.WriteAllText(marker, AppInfoService.BuildToken);
        }).OnFailure(ex => Log.Warning(ex, "Failed to prepare the web view's data folder"));
    }
}
