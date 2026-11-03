using System.Security.Cryptography;

namespace ClipboardSync.Services;

internal sealed record BlobWrite(string Hash, long Size);

/// <summary>
///     Content-addressed file storage under the clipboard data folder. A blob is named by the
///     SHA-256 of its contents, so identical files are stored once and a transfer can resume from
///     whatever bytes already arrived.
/// </summary>
internal static class BlobStore
{
    private const int BufferSize = 1024 * 1024;
    private static readonly string DefaultRoot = Path.Combine(Constants.DataDirectory, "clipboard", "blobs");
    private static readonly object WriteSync = new();
    private static string _root = DefaultRoot;
    private static int _writers;
    private static bool _migrating;

    public static string Root => _root;

    public static string DefaultDirectory => DefaultRoot;

    private static string PartialRoot => Path.Combine(_root, ".partial");

    public static string PathFor(string hash) => Path.Combine(_root, hash);

    public static string PartialPath(string hash) => Path.Combine(PartialRoot, hash + ".part");

    /// <summary>Uses the configured folder, falling back to the default when it cannot be created.</summary>
    public static void Configure(string? directory)
    {
        string next;

        try
        {
            next = Resolve(directory);
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Invalid blob folder {Directory}; using the default", directory);
            _root = DefaultRoot;
            return;
        }

        try
        {
            Directory.CreateDirectory(next);
            _root = next;
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Could not use the blob folder {Directory}; using the default",
                next);
            _root = DefaultRoot;
        }
    }

    /// <summary>Moves existing blobs to a new folder before it becomes the active store.</summary>
    public static async Task MoveToAsync(string? directory, CancellationToken token = default)
    {
        var next = Resolve(directory);

        if (string.Equals(next, _root, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsUnder(next, _root) || IsUnder(_root, next))
            throw new ArgumentException("The blob folder cannot contain the current one.", nameof(directory));

        lock (WriteSync)
        {
            if (_migrating)
                throw new InvalidOperationException("The blob folder is already being moved.");

            _migrating = true;
        }

        try
        {
            while (Volatile.Read(ref _writers) > 0)
                await Task.Delay(50, token);

            Directory.CreateDirectory(next);
            Directory.CreateDirectory(Path.Combine(next, ".partial"));
            CopyFiles(_root, next);
            CopyFiles(Path.Combine(_root, ".partial"), Path.Combine(next, ".partial"));
            var previous = _root;
            _root = next;
            DeleteFiles(previous);
            DeleteFiles(Path.Combine(previous, ".partial"));
        }
        finally
        {
            lock (WriteSync)
                _migrating = false;
        }
    }

    /// <summary>Keeps garbage collection from deleting blobs a capture or receive is still adding.</summary>
    public static IDisposable BeginWrite()
    {
        while (true)
        {
            lock (WriteSync)
            {
                if (_migrating)
                {
                    // The folder is being moved; wait for it to finish.
                }
                else
                {
                    _writers++;
                    return new WriteLease();
                }
            }

            Thread.Sleep(20);
        }
    }

    public static bool Exists(string hash) =>
        hash.Length == 64 && File.Exists(PathFor(hash));

    public static long Length(string hash)
    {
        var path = PathFor(hash);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    /// <summary>Copies a file into the blob store, hashing it while it is copied.</summary>
    public static BlobWrite Put(string sourcePath)
    {
        Directory.CreateDirectory(Root);
        var temporary = Path.Combine(Root, $".{Guid.NewGuid():N}.tmp");

        try
        {
            string hash;
            long size = 0;

            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       BufferSize, FileOptions.SequentialScan))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int read;

                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                    size += read;
                }

                hash = Convert.ToHexString(hasher.GetHashAndReset());
            }

            var final = PathFor(hash);

            if (File.Exists(final))
                File.Delete(temporary);
            else
                File.Move(temporary, final);

            return new(hash, size);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static long PartialLength(string hash)
    {
        var path = PartialPath(hash);
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    /// <summary>Writes one range at <paramref name="offset" /> of the partial blob.</summary>
    public static void AppendPartial(string hash, long offset, byte[] data)
    {
        Directory.CreateDirectory(PartialRoot);
        var path = PartialPath(hash);

        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.SequentialScan);

        if (stream.Length != offset)
        {
            if (stream.Length > offset)
                stream.SetLength(offset);
            else
                throw new IOException("The partial blob has a gap.");
        }

        stream.Seek(offset, SeekOrigin.Begin);
        stream.Write(data);
        stream.Flush(true);
    }

    /// <summary>Verifies the finished partial and moves it into the blob store.</summary>
    public static bool CompletePartial(string hash, long expectedSize)
    {
        var partial = PartialPath(hash);
        if (!File.Exists(partial) || new FileInfo(partial).Length != expectedSize)
            return false;

        using (var stream = File.OpenRead(partial))
        {
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (actual != hash)
            {
                TryDelete(partial);
                return false;
            }
        }

        var final = PathFor(hash);
        Directory.CreateDirectory(Root);

        if (File.Exists(final))
            File.Delete(partial);
        else
            File.Move(partial, final);

        return true;
    }

    public static void DeletePartial(string hash) => TryDelete(PartialPath(hash));

    /// <summary>Makes sure the single empty blob exists; zero-byte files still need a hash entry.</summary>
    public static void PutEmpty()
    {
        var hash = Convert.ToHexString(SHA256.HashData([]));
        var path = PathFor(hash);

        if (File.Exists(path))
            return;

        Directory.CreateDirectory(Root);
        File.WriteAllBytes(path, []);
    }

    public static void Delete(string hash)
    {
        TryDelete(PathFor(hash));
        TryDelete(PartialPath(hash));
    }

    /// <summary>Deletes blobs no record refers to, and partial uploads nobody resumed for a week.</summary>
    public static void GarbageCollect(IEnumerable<string> referenced)
    {
        if (Volatile.Read(ref _migrating) || Volatile.Read(ref _writers) > 0)
            return;

        var keep = referenced
            .Where(hash => hash.Length == 64)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (Directory.Exists(Root))
                foreach (var file in Directory.EnumerateFiles(Root))
                    if (!keep.Contains(Path.GetFileName(file)))
                        TryDelete(file);

            if (Directory.Exists(PartialRoot))
                foreach (var file in Directory.EnumerateFiles(PartialRoot))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(7))
                        TryDelete(file);
        }
        catch (Exception ex)
        {
            Constants.Logger.Debug(ex, "Failed to clean up the blob store");
        }
    }

    public static Stream OpenRead(string hash, long offset)
    {
        var stream = new FileStream(PathFor(hash), FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan);
        stream.Seek(offset, SeekOrigin.Begin);
        return stream;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Constants.Logger.Debug(ex, "Failed to delete {Path}", path);
        }
    }

    private static string Resolve(string? directory) =>
        string.IsNullOrWhiteSpace(directory)
            ? DefaultRoot
            : Path.GetFullPath(Expand(directory.Trim()));

    private static string Expand(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);

        if (expanded is "~" || expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[1..].TrimStart('/', '\\'));

        return expanded;
    }

    private static bool IsUnder(string path, string parent)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyFiles(string source, string destination)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, true);
        }
    }

    private static void DeleteFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory))
            TryDelete(file);
    }

    private sealed class WriteLease : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                lock (WriteSync)
                    _writers--;
        }
    }
}
