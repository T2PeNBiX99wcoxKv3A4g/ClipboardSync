using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ClipboardSync.Models;
using ClipboardSync.Security;

namespace ClipboardSync.Services;

/// <summary>A copied file selection that has been added to the blob store.</summary>
internal sealed record ClipboardFileSnapshot(
    ClipboardFileEntry[] Files,
    string[] Roots,
    long TotalBytes,
    string Signature);

/// <summary>
///     Turns a copied file selection into a manifest whose file entries point at content-addressed
///     blobs. Materialising an item later hardlinks those blobs into a folder with the original
///     names, so a file list can be put back on the clipboard without copying the bytes again.
/// </summary>
internal static class ClipboardFiles
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    public static string ViewFolder(string id) =>
        Path.Combine(Constants.DataDirectory, "clipboard", "items", id);

    /// <summary>
    ///     Enumerates the selection and stores every file as a blob. Returns null when there is
    ///     nothing usable; throws <see cref="FileLimitException" /> when the size or count is over
    ///     the configured limit.
    /// </summary>
    public static async Task<ClipboardFileSnapshot?> SnapshotAsync(IReadOnlyList<string> sources, long maxBytes,
        CancellationToken token = default)
    {
        using var writeLease = BlobStore.BeginWrite();
        var entries = new List<ClipboardFileEntry>();
        var roots = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signature = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long declared = 0;
        long total = 0;

        try
        {
            foreach (var source in sources)
            {
                var full = Path.GetFullPath(source.Trim());
                if (!File.Exists(full) && !Directory.Exists(full))
                    continue;

                var attributes = File.GetAttributes(full);

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var isFile = (attributes & FileAttributes.Directory) == 0;
                var isDirectory = !isFile;

                if (!isFile && !isDirectory)
                    continue;

                var name = SafeName(Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)));
                var root = Unique(used, name);
                roots.Add(root);

                if (isFile)
                    AddFile(full, root);
                else
                    AddDirectory(full, root);
            }

            if (entries.Count == 0)
                return null;

            foreach (var entry in entries.Where(entry => !entry.Directory))
            {
                var source = entry.SourcePath
                             ?? throw new IOException("A file entry has no source path.");

                var blob = await BlobStore.PutAsync(source, token);
                entry.Hash = blob.Hash;
                entry.Size = blob.Size;
                total += blob.Size;

                if (total > maxBytes)
                    throw new FileLimitException();

                signature.AppendData(Encoding.UTF8.GetBytes($"{entry.Path}\n{entry.Hash}\n"));
            }

            return new([.. entries], [.. roots], total,
                Convert.ToHexString(signature.GetHashAndReset()));
        }
        finally
        {
            signature.Dispose();
        }

        void AddFile(string full, string relative)
        {
            var length = new FileInfo(full).Length;
            declared += length;

            if (entries.Count >= PeerProtocol.MaxFiles || declared > maxBytes)
                throw new FileLimitException();

            entries.Add(new()
            {
                Path = relative,
                Size = length,
                Directory = false,
                SourcePath = full
            });
        }

        void AddDirectory(string full, string relative)
        {
            if (entries.Count >= PeerProtocol.MaxFiles)
                throw new FileLimitException();

            entries.Add(new()
            {
                Path = relative,
                Directory = true
            });

            signature.AppendData(Encoding.UTF8.GetBytes($"{relative}/\n"));

            string[] children;
            try
            {
                children = Directory.GetFileSystemEntries(full);
            }
            catch (Exception)
            {
                return;
            }

            foreach (var child in children.Order(StringComparer.OrdinalIgnoreCase))
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(child);
                }
                catch (Exception)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var name = SafeName(Path.GetFileName(child));
                var path = Unique(used, $"{relative}/{name}");

                if ((attributes & FileAttributes.Directory) != 0)
                    AddDirectory(child, path);
                else
                    AddFile(child, path);
            }
        }
    }

    /// <summary>
    ///     Builds a folder with the manifest's names, hardlinking the blobs. The clipboard gets these
    ///     paths, so a received item can be pasted as ordinary files.
    /// </summary>
    public static async Task<string> MaterializeAsync(ClipboardRecord record, CancellationToken token = default)
    {
        if (record.Files is not { Count: > 0 } files)
            throw new IOException("The file item has no manifest.");

        var folder = ViewFolder(record.Id);
        if (Directory.Exists(folder))
            return folder;

        var staging = folder + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);

        try
        {
            foreach (var entry in files)
            {
                var path = Resolve(staging, entry.Path)
                           ?? throw new IOException("The file item contains an unsafe path.");

                if (entry.Directory)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }

                var blob = entry.Hash is { Length: 64 } ? BlobStore.PathFor(entry.Hash) : null;
                if (blob is null || !File.Exists(blob))
                    throw new IOException("A blob that belongs to the item is missing.");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (!TryLink(blob, path))
                    await CopyFileAsync(blob, path, token);
            }

            var parent = Path.GetDirectoryName(folder)!;
            Directory.CreateDirectory(parent);

            if (Directory.Exists(folder))
                Directory.Delete(folder, true);

            Directory.Move(staging, folder);
            return folder;
        }
        catch
        {
            await TryDeleteDirectoryAsync(staging);
            throw;
        }
    }

    public static Task DeleteViewAsync(string id) => TryDeleteDirectoryAsync(ViewFolder(id));

    /// <summary>Maps a manifest path into <paramref name="folder" /> without allowing escapes.</summary>
    public static string? Resolve(string folder, string relative)
    {
        var path = relative.Replace('\\', '/');

        if (path.Length == 0 || path.StartsWith('/') || path.Split('/').Any(part => part is "" or "." or ".."))
            return null;

        var root = Path.GetFullPath(folder);
        var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1024L * 1024L * 1024L
            ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
            : bytes >= 1024L * 1024L
                ? $"{bytes / 1024d / 1024d:0.0} MB"
                : $"{Math.Max(1, bytes / 1024d):0} KB";

    private static bool TryLink(string source, string destination)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return CreateHardLink(destination, source, IntPtr.Zero);

            if (OperatingSystem.IsLinux())
                return link(source, destination) == 0;
        }
        catch (Exception)
        {
            // Fall back to a copy below.
        }

        return false;
    }

    private static Task TryDeleteDirectoryAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Constants.Logger.Debug(ex, "Failed to delete the view folder {Path}", path);
            }
        });

    private static async Task CopyFileAsync(string source, string destination, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, token);
    }

    private static string SafeName(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var value in name)
            builder.Append(value is < ' ' or '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '_'
                : value);

        var safe = builder.ToString().Trim().TrimEnd('.', ' ');
        if (safe.Length == 0)
            safe = "item";

        if (safe.Length > 120)
            safe = safe[..120];

        var stem = Path.GetFileNameWithoutExtension(safe);
        return ReservedNames.Contains(stem) ? "_" + safe : safe;
    }

    private static string Unique(HashSet<string> used, string relative)
    {
        if (used.Add(relative))
            return relative;

        var slash = relative.LastIndexOf('/');
        var dot = relative.LastIndexOf('.');
        var stem = dot > slash ? relative[..dot] : relative;
        var extension = dot > slash ? relative[dot..] : "";

        for (var index = 2;; index++)
        {
            var candidate = $"{stem} ({index}){extension}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName,
        IntPtr securityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldPath, string newPath);

    /// <summary>Raised when a copied selection is beyond the configured size or entry limits.</summary>
    internal sealed class FileLimitException : Exception;
}