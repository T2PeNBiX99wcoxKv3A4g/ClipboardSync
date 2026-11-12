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
    private static readonly Lock WriteSync = new();
    private static int _writers;
    private static bool _migrating;

    public static string Root { get; private set; } = DefaultDirectory;

    public static string DefaultDirectory { get; } = Path.Combine(Constants.DataDirectory, "clipboard", "blobs");

    private static string PartialRoot => Path.Combine(Root, ".partial");

    public static string PathFor(string hash) => Path.Combine(Root, hash);

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
            Root = DefaultDirectory;
            return;
        }

        try
        {
            Directory.CreateDirectory(next);
            Root = next;
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Could not use the blob folder {Directory}; using the default",
                next);
            Root = DefaultDirectory;
        }
    }

    /// <summary>Moves existing blobs to a new folder before it becomes the active store.</summary>
    public static async Task MoveToAsync(string? directory, CancellationToken token = default)
    {
        var next = Resolve(directory);

        if (string.Equals(next, Root, StringComparison.OrdinalIgnoreCase))
            return;

        if (IsUnder(next, Root) || IsUnder(Root, next))
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
            await CopyFilesAsync(Root, next, token);
            await CopyFilesAsync(Path.Combine(Root, ".partial"), Path.Combine(next, ".partial"), token);
            var previous = Root;
            Root = next;
            await DeleteFilesAsync(previous, token);
            await DeleteFilesAsync(Path.Combine(previous, ".partial"), token);
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
                if (_migrating)
                {
                    // The folder is being moved; wait for it to finish.
                }
                else
                {
                    _writers++;
                    return new WriteLease();
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
    public static async Task<BlobWrite> PutAsync(string sourcePath, CancellationToken token = default)
    {
        Directory.CreateDirectory(Root);
        var temporary = Path.Combine(Root, $".{Guid.NewGuid():N}.tmp");

        try
        {
            string hash;
            long size = 0;

            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int read;

                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
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
    public static async Task AppendPartialAsync(string hash, long offset, byte[] data,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(PartialRoot);
        var path = PartialPath(hash);

        await using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length != offset)
        {
            if (stream.Length > offset)
                stream.SetLength(offset);
            else
                throw new IOException("The partial blob has a gap.");
        }

        stream.Seek(offset, SeekOrigin.Begin);
        await stream.WriteAsync(data, token);
        await stream.FlushAsync(token);
    }

    /// <summary>Verifies the finished partial and moves it into the blob store.</summary>
    public static async Task<bool> CompletePartialAsync(string hash, long expectedSize,
        CancellationToken token = default)
    {
        var partial = PartialPath(hash);
        if (!File.Exists(partial) || new FileInfo(partial).Length != expectedSize)
            return false;

        string actual;
        await using (var stream = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read,
                         BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
                hasher.AppendData(buffer, 0, read);

            actual = Convert.ToHexString(hasher.GetHashAndReset());
        }

        if (actual != hash)
        {
            TryDelete(partial);
            return false;
        }

        var final = PathFor(hash);
        Directory.CreateDirectory(Root);

        if (File.Exists(final))
            File.Delete(partial);
        else
            File.Move(partial, final);

        return true;
    }

    public static Task DeletePartialAsync(string hash)
    {
        TryDelete(PartialPath(hash));
        return Task.CompletedTask;
    }

    /// <summary>Makes sure the single empty blob exists; zero-byte files still need a hash entry.</summary>
    public static async Task PutEmptyAsync()
    {
        var hash = Convert.ToHexString(SHA256.HashData([]));
        var path = PathFor(hash);

        if (File.Exists(path))
            return;

        Directory.CreateDirectory(Root);
        await File.WriteAllBytesAsync(path, []);
    }

    public static Task DeleteAsync(string hash)
    {
        TryDelete(PathFor(hash));
        TryDelete(PartialPath(hash));
        return Task.CompletedTask;
    }

    /// <summary>Deletes blobs no record refers to, and partial uploads nobody resumed for a week.</summary>
    public static Task GarbageCollectAsync(IEnumerable<string> referenced, CancellationToken token = default)
    {
        if (Volatile.Read(ref _migrating) || Volatile.Read(ref _writers) > 0)
            return Task.CompletedTask;

        var keep = referenced
            .Where(hash => hash.Length == 64)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.Run(() =>
        {
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
        }, token);
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
            ? DefaultDirectory
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

    private static async Task CopyFilesAsync(string source, string destination, CancellationToken token)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));

            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            await input.CopyToAsync(output, token);
        }
    }

    private static Task DeleteFilesAsync(string directory, CancellationToken token) =>
        Task.Run(() =>
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var file in Directory.EnumerateFiles(directory))
                TryDelete(file);
        }, token);

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