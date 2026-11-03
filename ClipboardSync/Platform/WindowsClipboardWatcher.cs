using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ClipboardSync.Security;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Interop;
using PhotinoNetUtils.Utils;

namespace ClipboardSync.Platform;

/// <summary>
///     Watches the Windows clipboard from a hidden window on a dedicated STA thread. The OS tells the
///     window when the clipboard changes, so nothing polls it. A read that cannot get the lock is
///     retried a couple of times and then skipped.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboardWatcher : Singleton<WindowsClipboardWatcher>, IClipboardWatcher
{
    private const uint WmDestroy = 0x0002;
    private const uint GmemMoveable = 0x0002;
    private const int ReadAttempts = 6;
    private const int ReadRetryMs = 20;

    private readonly string _className = $"ClipboardSync.Clipboard.{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _clipboardGate = new(1, 1);
    private readonly ManualResetEventSlim _ready = new();
    private IntPtr _hwnd = IntPtr.Zero;
    private string? _lastImageHash;
    private string? _lastText;
    private bool _listening;

    private Thread? _thread;
    private NativeMethods.WndProcDelegate? _wndProc;

    public event Action<string>? TextCopied;

    public event Action<byte[]>? ImageCopied;

    public void Start()
    {
        EnsureThread();

        if (_listening || !_ready.Wait(TimeSpan.FromSeconds(2)))
            return;

        _listening = NativeMethods.AddClipboardFormatListener(_hwnd);
        if (_listening)
            Log.Information("Watching the Windows clipboard");
    }

    public void Stop()
    {
        if (!_listening)
            return;

        _listening = false;

        if (_hwnd != IntPtr.Zero)
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
    }

    public void Dispose() => Stop();

    public string? ReadText()
    {
        for (var attempt = 0; attempt < ReadAttempts; attempt++)
        {
            if (TryReadText(out var text))
                return text;

            Thread.Sleep(ReadRetryMs);
        }

        Log.Debug("The clipboard stayed locked; skipping this change");
        return null;
    }

    public bool WriteText(string text)
    {
        if (!_clipboardGate.Wait(2000))
            return false;

        try
        {
            for (var attempt = 0; attempt < ReadAttempts; attempt++)
            {
                try
                {
                    if (TryWriteText(text))
                    {
                        _lastText = text;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not write the clipboard text");
                }

                Thread.Sleep(ReadRetryMs);
            }

            Log.Warning("The clipboard stayed locked; could not write the received text");
            return false;
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
            Log.Warning("The clipboard was busy; could not write the received image");
            return false;
        }

        try
        {
            for (var attempt = 0; attempt < ReadAttempts; attempt++)
            {
                try
                {
                    if (TryWriteImage(png))
                    {
                        _lastImageHash = Convert.ToHexString(SHA256.HashData(png));
                        _lastText = null;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not write the clipboard image");
                }

                Thread.Sleep(ReadRetryMs);
            }

            Log.Warning("The clipboard stayed locked; could not write the received image");
            return false;
        }
        finally
        {
            _clipboardGate.Release();
        }
    }

    private void EnsureThread()
    {
        if (_thread is not null)
            return;

        _thread = new(ThreadProc)
        {
            IsBackground = true,
            Name = "ClipboardSync.Clipboard"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ThreadProc()
    {
        var hInstance = NativeMethods.GetModuleHandle(null);
        _wndProc = WindowProc;

        var windowClass = new NativeMethods.WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className
        };
        NativeMethods.RegisterClassEx(ref windowClass);

        _hwnd = NativeMethods.CreateWindowEx(
            0, _className, Constants.Name, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        _ready.Set();

        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        _hwnd = IntPtr.Zero;
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmClipboardUpdate:
                Publish();
                return IntPtr.Zero;

            case WmDestroy:
                NativeMethods.PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Publish()
    {
        // Our own writes put a value on the clipboard too; that must not echo into the session.
        if (IsOwnedByThisProcess())
            return;

        byte[]? png = null;
        string? text = null;

        if (!_clipboardGate.Wait(1_000))
            return;

        try
        {
            if (HasImageFormat())
                TryReadImagePng(out png);
            else
                text = ReadText();
        }
        finally
        {
            _clipboardGate.Release();
        }

        if (png is not null)
        {
            var hash = Convert.ToHexString(SHA256.HashData(png));
            if (hash == _lastImageHash)
                return;

            _lastImageHash = hash;
            _lastText = null;
            Try.Run(() => ImageCopied?.Invoke(png))
                .OnFailure(ex => Log.Warning(ex, "Clipboard image handler failed"));
            return;
        }

        if (string.IsNullOrEmpty(text) || text == _lastText)
            return;

        _lastText = text;
        Try.Run(() => TextCopied?.Invoke(text)).OnFailure(ex => Log.Warning(ex, "Clipboard handler failed"));
    }

    private static bool IsOwnedByThisProcess()
    {
        var owner = NativeMethods.GetClipboardOwner();
        return owner != IntPtr.Zero &&
               NativeMethods.GetWindowThreadProcessId(owner, out var processId) != 0 &&
               processId == Environment.ProcessId;
    }

    private static bool HasImageFormat()
    {
        if (IsClipboardFormatAvailable(2) || IsClipboardFormatAvailable(8) || IsClipboardFormatAvailable(17))
            return true;

        var png = RegisterClipboardFormat("PNG");
        return png != 0 && IsClipboardFormatAvailable(png);
    }

    private static bool TryReadImagePng(out byte[] png)
    {
        png = [];

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            return TryReadPng(out png) || TryReadBitmap(out png) || TryReadDib(out png);
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryReadPng(out byte[] png)
    {
        png = [];

        var format = RegisterClipboardFormat("PNG");
        if (format == 0)
            return false;

        var handle = NativeMethods.GetClipboardData(format);
        if (handle == IntPtr.Zero)
            return false;

        var size = GlobalSize(handle).ToUInt64();
        if (size <= 8 || size > (ulong)ClipboardLimits.MaxImageBytes)
            return false;

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
            return false;

        try
        {
            var bytes = new byte[(int)size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);

            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                return false;

            png = bytes;
            return true;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static bool TryReadBitmap(out byte[] png)
    {
        png = [];

        var handle = NativeMethods.GetClipboardData(2);
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            using var image = Image.FromHbitmap(handle);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            png = stream.ToArray();
            return png.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadDib(out byte[] png)
    {
        png = [];

        var handle = NativeMethods.GetClipboardData(8);
        if (handle == IntPtr.Zero)
            return false;

        var size = GlobalSize(handle).ToUInt64();
        if (size <= 40 || size > (ulong)ClipboardLimits.MaxImageBytes)
            return false;

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
            return false;

        try
        {
            var dib = new byte[(int)size];
            Marshal.Copy(pointer, dib, 0, dib.Length);

            var headerSize = BitConverter.ToUInt32(dib, 0);
            var bitCount = BitConverter.ToUInt16(dib, 14);
            var colorsUsed = BitConverter.ToUInt32(dib, 32);
            var palette = bitCount <= 8
                ? (int)(colorsUsed == 0 ? 1u << bitCount : colorsUsed) * 4
                : 0;
            var offset = 14 + (int)headerSize + palette;

            using var stream = new MemoryStream(14 + dib.Length);
            stream.WriteByte((byte)'B');
            stream.WriteByte((byte)'M');
            stream.Write(BitConverter.GetBytes(14 + dib.Length));
            stream.Write(BitConverter.GetBytes(0));
            stream.Write(BitConverter.GetBytes(offset));
            stream.Write(dib);
            stream.Position = 0;

            using var image = Image.FromStream(stream);
            using var output = new MemoryStream();
            image.Save(output, ImageFormat.Png);
            png = output.ToArray();
            return png.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    private static bool TryWriteImage(byte[] png)
    {
        using var stream = new MemoryStream(png);
        using var source = Image.FromStream(stream);
        using var bitmap = new Bitmap(source);

        using var bitmapStream = new MemoryStream();
        bitmap.Save(bitmapStream, ImageFormat.Bmp);
        var bmp = bitmapStream.ToArray();
        if (bmp.Length <= 14)
            return false;

        var dib = bmp[14..];

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            return false;

        var pngFormat = RegisterClipboardFormat("PNG");
        var pngHandle = IntPtr.Zero;
        var dibHandle = IntPtr.Zero;

        try
        {
            if (!EmptyClipboard())
                return false;

            var wrote = false;

            if (pngFormat != 0)
            {
                pngHandle = GlobalAlloc(GmemMoveable, (UIntPtr)png.Length);
                if (pngHandle != IntPtr.Zero)
                {
                    var pointer = NativeMethods.GlobalLock(pngHandle);
                    if (pointer != IntPtr.Zero)
                    {
                        Marshal.Copy(png, 0, pointer, png.Length);
                        NativeMethods.GlobalUnlock(pngHandle);
                    }

                    if (SetClipboardData(pngFormat, pngHandle) != IntPtr.Zero)
                    {
                        pngHandle = IntPtr.Zero;
                        wrote = true;
                    }
                }
            }

            // CF_DIB is the format every Windows application understands; the PNG format above is
            // only a convenience for modern apps. A plain CF_BITMAP handle was not readable by
            // System.Windows.Forms.Clipboard.GetImage on this machine.
            dibHandle = GlobalAlloc(GmemMoveable, (UIntPtr)dib.Length);
            if (dibHandle != IntPtr.Zero)
            {
                var pointer = NativeMethods.GlobalLock(dibHandle);
                if (pointer != IntPtr.Zero)
                {
                    Marshal.Copy(dib, 0, pointer, dib.Length);
                    NativeMethods.GlobalUnlock(dibHandle);
                }

                if (SetClipboardData(8, dibHandle) != IntPtr.Zero)
                {
                    dibHandle = IntPtr.Zero;
                    wrote = true;
                }
            }

            return wrote;
        }
        finally
        {
            if (pngHandle != IntPtr.Zero)
                GlobalFree(pngHandle);
            if (dibHandle != IntPtr.Zero)
                GlobalFree(dibHandle);

            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryReadText(out string? text)
    {
        text = null;

        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            var handle = NativeMethods.GetClipboardData(NativeMethods.CfUnicodeText);
            if (handle == IntPtr.Zero)
                return true;

            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return true;

            try
            {
                text = Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryWriteText(string text)
    {
        if (!NativeMethods.OpenClipboard(IntPtr.Zero))
            return false;

        IntPtr handle = IntPtr.Zero;

        try
        {
            if (!EmptyClipboard())
                return false;

            var bytes = System.Text.Encoding.Unicode.GetBytes(text + '\0');
            handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
            if (handle == IntPtr.Zero)
                return false;

            var pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                GlobalFree(handle);
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            if (SetClipboardData(NativeMethods.CfUnicodeText, handle) != IntPtr.Zero)
            {
                handle = IntPtr.Zero;
                return true;
            }

            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                GlobalFree(handle);

            NativeMethods.CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);
}
