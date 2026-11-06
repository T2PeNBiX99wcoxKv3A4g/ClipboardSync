using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Platform;

/// <summary>
///     Watches the Linux clipboard through Klipper, <c>wl-paste</c> on Wayland, or GTK's clipboard
///     signal. None of the paths blocks the UI thread. Writing uses the matching command-line tool.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxClipboardWatcher : Singleton<LinuxClipboardWatcher>, IClipboardWatcher
{
    private const byte Separator = 0x01;
    private const int MaxValueLength = 1024 * 1024;

    private readonly SemaphoreSlim _clipboardGate = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private bool _imageBaselineSet;
    private CancellationTokenSource? _imageCancellation;
    private bool _fileBaselineSet;
    private CancellationTokenSource? _fileCancellation;
    private string? _lastImageHash;
    private string? _lastFiles;
    private string? _lastText;
    private Process? _process;
    private bool _started;

    public event Action<string>? TextCopied;

    public event Action<byte[]>? ImageCopied;

    public event Action<string[]>? FilesCopied;

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _imageBaselineSet = false;
        _fileBaselineSet = false;
        _imageCancellation = new();
        _fileCancellation = new();
        _ = Task.Run(() => ImageLoopAsync(_imageCancellation.Token));
        _ = Task.Run(() => FileLoopAsync(_fileCancellation.Token));

        if (TryWatchWithKlipper())
            return;

        if (TryWatchWithWlPaste())
            return;

        if (!LinuxGtk.WatchClipboard(OnTextCopied))
            Log.Warning("Clipboard watching is unavailable on this desktop");
        else
            Log.Information("Watching the clipboard through GTK");
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _imageCancellation?.Cancel();
        _fileCancellation?.Cancel();

        Try.Run(() =>
        {
            if (_process is { HasExited: false } process)
                process.Kill(true);
        }).OnFailure(ex => Log.Debug(ex, "Failed to stop the clipboard watcher"));

        _process = null;
        _lastText = null;
        _lastImageHash = null;
        _lastFiles = null;
        _imageBaselineSet = false;
        _fileBaselineSet = false;
        _started = false;
        _cancellation?.Dispose();
        _cancellation = null;
        _imageCancellation?.Dispose();
        _imageCancellation = null;
        _fileCancellation?.Dispose();
        _fileCancellation = null;

        LinuxGtk.StopWatchingClipboard();
    }

    public void Dispose() => Stop();

    public string? ReadText() =>
        Try.Run(() => ReadWithCommand() ?? ReadWithKlipper()).GetOrElse(ex =>
        {
            Log.Debug(ex, "Could not read the clipboard");
            return null;
        });

    public bool WriteText(string text)
    {
        if (!_clipboardGate.Wait(2000))
            return false;

        try
        {
            var written = Try.Run(() => WriteWithCommand(text)).GetOrElse(ex =>
            {
                Log.Debug(ex, "Could not write the clipboard");
                return false;
            });

            if (written)
                _lastText = text;
            else
                Log.Warning("Could not write the clipboard text");

            return written;
        }
        finally
        {
            _clipboardGate.Release();
        }
    }

    public bool WriteImage(byte[] png)
    {
        if (!_clipboardGate.Wait(2000))
        {
            Log.Warning("The clipboard was busy; could not write the image");
            return false;
        }

        try
        {
            var written = Try.Run(() => WriteImageWithCommand(png)).GetOrElse(ex =>
            {
                Log.Debug(ex, "Could not write the clipboard image");
                return false;
            });

            if (written)
                _lastImageHash = Convert.ToHexString(SHA256.HashData(png));
            else
                Log.Warning("Could not write the clipboard image");

            return written;
        }
        finally
        {
            _clipboardGate.Release();
        }
    }

    public bool WriteFiles(IReadOnlyList<string> paths)
    {
        if (!_clipboardGate.Wait(2000))
            return false;

        try
        {
            var valid = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray();
            if (valid.Length == 0)
                return false;

            var written = Try.Run(() => WriteFilesWithCommand(valid)).GetOrElse(ex =>
            {
                Log.Debug(ex, "Could not write the clipboard file list");
                return false;
            });

            if (written)
            {
                _lastFiles = string.Join('\n', valid);
                _lastImageHash = null;
                _lastText = null;
            }
            else
                Log.Warning("Could not write the clipboard file list");

            return written;
        }
        finally
        {
            _clipboardGate.Release();
        }
    }

    private async Task ImageLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                await _clipboardGate.WaitAsync(token);
                byte[]? png;

                try
                {
                    png = ReadImageWithCommand();
                }
                finally
                {
                    _clipboardGate.Release();
                }

                if (png is { Length: > 0 } && png.Length <= ClipboardLimits.MaxImageBytes)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(png));

                    // The clipboard already has a value when the app starts; that is not a new copy.
                    if (!_imageBaselineSet)
                    {
                        _imageBaselineSet = true;
                        _lastImageHash = hash;
                    }
                    else if (hash != _lastImageHash)
                    {
                        _lastImageHash = hash;
                        Try.Run(() => ImageCopied?.Invoke(png))
                            .OnFailure(ex => Log.Warning(ex, "Clipboard image handler failed"));
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(1_500), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The image clipboard watcher stopped");
                break;
            }
    }

    private async Task FileLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                var files = ReadFilesWithCommand();

                if (files is { Length: > 0 })
                {
                    var signature = string.Join('\n', files);

                    if (!_fileBaselineSet)
                    {
                        _fileBaselineSet = true;
                        _lastFiles = signature;
                    }
                    else if (signature != _lastFiles)
                    {
                        _lastFiles = signature;
                        _lastText = null;
                        _lastImageHash = null;
                        Try.Run(() => FilesCopied?.Invoke(files))
                            .OnFailure(ex => Log.Warning(ex, "Clipboard file handler failed"));
                    }
                }

                await Task.Delay(TimeSpan.FromMilliseconds(1_500), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "The clipboard file watcher stopped");
                break;
            }
    }

    private bool TryWatchWithKlipper()
    {
        if (!HasKlipper())
            return false;

        return Try.Run(() =>
        {
            var process = Process.Start(Command("gdbus", "monitor", "--session", "--dest", "org.kde.klipper",
                "--object-path", "/klipper"));

            if (process is null)
                return false;

            _process = process;
            _cancellation = new();
            _ = Task.Run(() => PumpKlipperAsync(process, _cancellation.Token));
            Log.Information("Watching the clipboard through Klipper");
            return true;
        }).GetOrElse(ex =>
        {
            Log.Information(ex, "Could not watch the clipboard through Klipper: {Message}", ex.Message);
            return false;
        });
    }

    private async Task PumpKlipperAsync(Process process, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(token);
                if (line is null)
                    break;

                if (line.Contains("clipboardHistoryUpdated", StringComparison.Ordinal))
                    Report(ReadWithCommand() ?? ReadWithKlipper());
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "The Klipper clipboard watcher stopped");
        }
    }

    private static bool HasKlipper() =>
        Run("gdbus",
        [
            "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path",
            "/org/freedesktop/DBus", "--method", "org.freedesktop.DBus.NameHasOwner", "org.kde.klipper"
        ])?.Contains("true", StringComparison.Ordinal) == true;

    private static string? ReadWithKlipper() =>
        Unwrap(Run("gdbus",
        [
            "call", "--session", "--dest", "org.kde.klipper", "--object-path", "/klipper",
            "--method", "org.kde.klipper.klipper.getClipboardContents"
        ]));

    private static string? Unwrap(string? output)
    {
        var text = output?.Trim();
        if (text is not { Length: >= 4 } || text[0] != '(' || text[^1] != ')' || text[^2] != ',')
            return null;

        // gdbus picks the quote that needs fewer escapes, so a string containing ' comes back as
        // ("...",) instead of ('...',). Both forms carry the same text.
        var quote = text[1];
        if (quote is not ('\'' or '"') || text[^3] != quote)
            return null;

        return Unescape(text[2..^3]);
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length)
            {
                builder.Append(value[index]);
                continue;
            }

            index++;
            builder.Append(value[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => value[index]
            });
        }

        return builder.ToString();
    }

    private void Report(string? text)
    {
        if (string.IsNullOrEmpty(text) || text == _lastText || LooksLikeFileList(text))
            return;

        _lastText = text;
        OnTextCopied(text);
    }

    private static bool LooksLikeFileList(string text) =>
        text.Split('\n').Any(line => line.TrimStart().StartsWith("file://", StringComparison.OrdinalIgnoreCase));

    private bool TryWatchWithWlPaste()
    {
        if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not { Length: > 0 })
            return false;

        return Try.Run(() =>
        {
            var startInfo = new ProcessStartInfo("wl-paste")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--type");
            startInfo.ArgumentList.Add("text");
            startInfo.ArgumentList.Add("--watch");
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("cat; printf '\\001'");

            var process = Process.Start(startInfo);
            if (process is null)
                return false;

            _process = process;
            _cancellation = new();
            _ = Task.Run(() => PumpAsync(process, _cancellation.Token));
            Log.Information("Watching the clipboard through wl-paste");
            return true;
        }).GetOrElse(ex =>
        {
            Log.Information(ex, "Could not watch the clipboard through wl-paste: {Message}", ex.Message);
            return false;
        });
    }

    private async Task PumpAsync(Process process, CancellationToken token)
    {
        var buffer = new byte[4096];
        var current = new List<byte>();

        try
        {
            var stream = process.StandardOutput.BaseStream;
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read == 0)
                    break;

                for (var index = 0; index < read; index++)
                {
                    var value = buffer[index];
                    if (value == Separator)
                    {
                        ReportValue(current);
                        current.Clear();
                    }
                    else if (current.Count < MaxValueLength)
                        current.Add(value);
                    else
                        current.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "The clipboard watcher stopped");
        }
    }

    private void ReportValue(List<byte> value)
    {
        if (value.Count > 0)
            OnTextCopied(Encoding.UTF8.GetString(value.ToArray()));
    }

    private void OnTextCopied(string text) =>
        Try.Run(() => TextCopied?.Invoke(text)).OnFailure(ex => Log.Warning(ex, "Clipboard handler failed"));

    private static ProcessStartInfo Command(string command, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static string? ReadWithCommand()
    {
        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-paste", ["--no-newline", "--type", "text"]),
            ("xclip", ["-selection", "clipboard", "-o"]),
            ("xsel", ["--clipboard", "--output"])
        ];

        foreach (var (command, arguments) in candidates)
            if (Run(command, arguments) is { Length: > 0 } text)
                return text;

        return null;
    }

    private static bool WriteWithCommand(string text)
    {
        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-copy", ["--type", "text/plain"]),
            ("xclip", ["-selection", "clipboard", "-in"]),
            ("xsel", ["--clipboard", "--input"])
        ];

        foreach (var (command, arguments) in candidates)
            if (RunWithInput(command, arguments, text))
                return true;

        return false;
    }

    private static string[]? ReadFilesWithCommand()
    {
        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-paste", ["--no-newline", "--type", "text/uri-list"]),
            ("xclip", ["-selection", "clipboard", "-t", "text/uri-list", "-o"])
        ];

        foreach (var (command, arguments) in candidates)
        {
            var value = Run(command, arguments);
            if (string.IsNullOrEmpty(value))
                continue;

            var files = ParseFileList(value);
            if (files.Length > 0)
                return files;
        }

        return null;
    }

    private static string[] ParseFileList(string value)
    {
        var paths = new List<string>();

        foreach (var raw in value.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || !uri.IsFile)
                continue;

            var path = uri.LocalPath;
            if (File.Exists(path) || Directory.Exists(path))
                paths.Add(path);
        }

        return [.. paths];
    }

    private static bool WriteFilesWithCommand(string[] paths)
    {
        var value = string.Join("\r\n", paths.Select(path => new Uri(path).AbsoluteUri)) + "\r\n";

        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-copy", ["--type", "text/uri-list"]),
            ("xclip", ["-selection", "clipboard", "-t", "text/uri-list", "-in"])
        ];

        foreach (var (command, arguments) in candidates)
            if (RunWithInput(command, arguments, value))
                return true;

        return false;
    }

    private static byte[]? ReadImageWithCommand()
    {
        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-paste", ["--type", "image/png"]),
            ("xclip", ["-selection", "clipboard", "-t", "image/png", "-o"])
        ];

        foreach (var (command, arguments) in candidates)
            if (RunBytes(command, arguments) is { Length: > 0 } bytes)
                return bytes;

        return null;
    }

    private static bool WriteImageWithCommand(byte[] png)
    {
        (string Command, string[] Arguments)[] candidates =
        [
            ("wl-copy", ["--type", "image/png"]),
            ("xclip", ["-selection", "clipboard", "-t", "image/png", "-in"])
        ];

        foreach (var (command, arguments) in candidates)
            if (RunBytesWithInput(command, arguments, png))
                return true;

        return false;
    }

    private static string? Run(string command, string[] arguments)
    {
        var process = Try.Run(() => Process.Start(Command(command, arguments))).GetOrNull();
        if (process is null)
            return null;

        using (process)
        {
            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(2000) && process.ExitCode == 0 ? output : null;
        }
    }

    private static bool RunWithInput(string command, string[] arguments, string input)
    {
        var startInfo = Command(command, arguments);
        startInfo.RedirectStandardInput = true;

        var process = Try.Run(() => Process.Start(startInfo)).GetOrNull();
        if (process is null)
            return false;

        using (process)
            try
            {
                process.StandardInput.Write(input);
                process.StandardInput.Close();
                return process.WaitForExit(2000) && process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
    }

    private static byte[]? RunBytes(string command, string[] arguments)
    {
        var process = Try.Run(() => Process.Start(Command(command, arguments))).GetOrNull();
        if (process is null)
            return null;

        using (process)
        {
            using var output = new MemoryStream();
            var buffer = new byte[81920];

            try
            {
                var stream = process.StandardOutput.BaseStream;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > ClipboardLimits.MaxImageBytes)
                    {
                        Try.Run(() => process.Kill(true)).OnFailure(_ => { });
                        return null;
                    }

                    output.Write(buffer, 0, read);
                }

                return process.WaitForExit(2000) && process.ExitCode == 0 && output.Length > 0
                    ? output.ToArray()
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static bool RunBytesWithInput(string command, string[] arguments, byte[] input)
    {
        var startInfo = Command(command, arguments);
        startInfo.RedirectStandardInput = true;

        var process = Try.Run(() => Process.Start(startInfo)).GetOrNull();
        if (process is null)
            return false;

        using (process)
            try
            {
                process.StandardInput.BaseStream.Write(input, 0, input.Length);
                process.StandardInput.Close();
                process.WaitForExit(3000);
                return !process.HasExited || process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
    }
}