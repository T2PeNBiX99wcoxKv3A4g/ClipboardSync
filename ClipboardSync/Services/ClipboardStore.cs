using System.Collections.Concurrent;
using System.Text.Json;
using ClipboardSync.Messaging;
using ClipboardSync.Models;
using ClipboardSync.Security;
using Microsoft.Data.Sqlite;

namespace ClipboardSync.Services;

/// <summary>
///     The replicated clipboard board in SQLite. Reads open their own connection and never take the
///     write gate, so the interface and background work can read while a write is in progress. Only
///     the write methods are serialized. Delivery state and file manifests are stored as JSON columns;
///     the rows themselves remain easy to inspect with any SQLite client.
/// </summary>
internal sealed class ClipboardStore : Singleton<ClipboardStore>
{
    private const string CreateSchema =
        """
        CREATE TABLE IF NOT EXISTS Items (
            Id TEXT NOT NULL CONSTRAINT PK_Items PRIMARY KEY,
            OriginDeviceId TEXT NOT NULL,
            OriginDeviceName TEXT NOT NULL,
            Kind TEXT NOT NULL,
            Content TEXT NOT NULL,
            Meta TEXT NULL,
            DataFile TEXT NULL,
            Hash TEXT NULL,
            At INTEGER NOT NULL,
            TotalBytes INTEGER NOT NULL DEFAULT 0,
            Progress INTEGER NOT NULL DEFAULT 0,
            Files TEXT NULL,
            Roots TEXT NULL,
            PendingFor TEXT NOT NULL DEFAULT '[]',
            DeliveredTo TEXT NOT NULL DEFAULT '[]',
            FailedFor TEXT NOT NULL DEFAULT '[]'
        );
        CREATE INDEX IF NOT EXISTS IX_Items_At ON Items (At DESC);
        CREATE TABLE IF NOT EXISTS Meta (
            Key TEXT NOT NULL CONSTRAINT PK_Meta PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;

    private const string SelectColumns =
        "Id, OriginDeviceId, OriginDeviceName, Kind, Content, Meta, DataFile, Hash, At, TotalBytes, Progress, Files, Roots, PendingFor, DeliveredTo, FailedFor";

    private static readonly string DatabasePath = Path.Combine(Constants.DataDirectory, "history.db");
    private static readonly string LegacyPath = Path.Combine(Constants.DataDirectory, "history.json");

    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, long> _progress = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ClipboardStore()
    {
        Directory.CreateDirectory(Constants.DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
    }

    /// <summary>Items at or before this time were cleared and must not come back.</summary>
    public long ClearAt { get; private set; }

    /// <summary>
    ///     Creates the schema, imports an older history.json once, and removes interrupted incoming
    ///     items. The entry point waits for this because the rest of the app expects a ready store.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = CreateSchema;
            await schema.ExecuteNonQueryAsync();
        }

        await MigrateLegacyAsync(connection);
        ClearAt = await ReadClearAtAsync(connection);
        await CleanupAsync(connection);
    }

    /// <summary>Newest first. No write gate: SQLite readers see the last committed snapshot.</summary>
    public async Task<IReadOnlyList<ClipboardRecord>> SnapshotAsync()
    {
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM Items ORDER BY At DESC";

            var items = new List<ClipboardRecord>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                items.Add(OverlayProgress(ReadRecord(reader)));

            return items;
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Failed to read the clipboard history");
            return [];
        }
    }

    public async Task<ClipboardRecord?> FindAsync(string id)
    {
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM Items WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);

            await using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync() ? OverlayProgress(ReadRecord(reader)) : null;
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Failed to read a clipboard item");
            return null;
        }
    }

    /// <summary>Clears every item and its local files, and remembers when it happened.</summary>
    public async Task<long> ClearAsync()
    {
        await _writeGate.WaitAsync();
        try
        {
            return await ClearLockedAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Applies a clear from a peer only when it is newer than the local one.</summary>
    public async Task<bool> ClearIfNewerAsync(long at)
    {
        await _writeGate.WaitAsync();
        try
        {
            if (at <= ClearAt)
                return false;

            await ClearLockedAsync(at);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<long> ClearLockedAsync(long at)
    {
        await using var connection = await OpenAsync();
        using var transaction = connection.BeginTransaction();

        var removed = new List<(string Id, string? DataFile)>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, DataFile FROM Items";

            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                removed.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Items";
            await delete.ExecuteNonQueryAsync();
        }

        await using (var meta = connection.CreateCommand())
        {
            meta.Transaction = transaction;
            meta.CommandText =
                "INSERT INTO Meta (Key, Value) VALUES ('history.clearedAt', $value) " +
                "ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
            meta.Parameters.AddWithValue("$value", at.ToString());
            await meta.ExecuteNonQueryAsync();
        }

        transaction.Commit();
        ClearAt = at;
        _progress.Clear();

        foreach (var (id, dataFile) in removed)
        {
            await DeleteBinaryAsync(dataFile);
            await ClipboardFiles.DeleteViewAsync(id);
        }

        await BlobStore.GarbageCollectAsync([]);
        return at;
    }

    private static async Task<long> ReadClearAtAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Meta WHERE Key = 'history.clearedAt'";

        var value = await command.ExecuteScalarAsync();
        return value is string text && long.TryParse(text, out var at) ? at : 0;
    }

    /// <summary>Adds an item once. Returns false when the id is already known or the item predates a clear.</summary>
    public async Task<bool> AddAsync(ClipboardRecord record)
    {
        if (record.At <= ClearAt)
            return false;

        await _writeGate.WaitAsync();
        try
        {
            if (record.At <= ClearAt)
                return false;

            await using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT OR IGNORE INTO Items
                    (Id, OriginDeviceId, OriginDeviceName, Kind, Content, Meta, DataFile, Hash, At,
                     TotalBytes, Progress, Files, Roots, PendingFor, DeliveredTo, FailedFor)
                VALUES
                    ($id, $originId, $originName, $kind, $content, $meta, $dataFile, $hash, $at,
                     $totalBytes, $progress, $files, $roots, $pending, $delivered, $failed);
                """;

            AddRecordParameters(command, record);
            var added = await command.ExecuteNonQueryAsync() > 0;

            if (added)
                await TrimAsync(connection, transaction);

            transaction.Commit();
            return added;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task MarkPendingAsync(string id, string peerId) => UpdateDeliveryAsync(id, peerId, DeliveryState.Pending);

    public Task MarkDeliveredAsync(string id, string peerId) => UpdateDeliveryAsync(id, peerId, DeliveryState.Delivered);

    public Task MarkFailedAsync(string id, string peerId) => UpdateDeliveryAsync(id, peerId, DeliveryState.Failed);

    /// <summary>Transient progress for the interface; it is not written on every chunk.</summary>
    public void SetProgress(string id, long progress) => _progress[id] = progress;

    /// <summary>Marks a received file item complete.</summary>
    public async Task CompleteFileAsync(string id)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Items SET Progress = TotalBytes WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
            _progress.TryRemove(id, out _);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Forgets a device's delivery state when it is unpaired.</summary>
    public async Task ForgetPeerAsync(string peerId)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            await using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = $"SELECT {SelectColumns} FROM Items";

            var changed = new List<ClipboardRecord>();
            await using (var reader = await select.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    changed.Add(ReadRecord(reader));

            foreach (var record in changed)
            {
                var touched = record.PendingFor.RemoveAll(peer => peer == peerId) > 0;
                touched |= record.DeliveredTo.RemoveAll(peer => peer == peerId) > 0;
                touched |= record.FailedFor.RemoveAll(peer => peer == peerId) > 0;

                if (touched)
                    await UpdateAsync(connection, transaction, record);
            }

            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Removes an item and its local files.</summary>
    public async Task RemoveAsync(string id)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            var record = await ReadOneAsync(connection, transaction, id);

            if (record is null)
            {
                transaction.Commit();
                return;
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM Items WHERE Id = $id";
                command.Parameters.AddWithValue("$id", id);
                await command.ExecuteNonQueryAsync();
            }

            transaction.Commit();
            await DeleteRecordFilesAsync(record);
            _progress.TryRemove(id, out _);
            await BlobStore.GarbageCollectAsync(await ReadHashesAsync());
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Drops blobs no item refers to, for example after a refused capture.</summary>
    public async Task CollectGarbageAsync() =>
        await BlobStore.GarbageCollectAsync(await ReadHashesAsync());

    private async Task UpdateDeliveryAsync(string id, string peerId, DeliveryState state)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var connection = await OpenAsync();
            using var transaction = connection.BeginTransaction();
            var record = await ReadOneAsync(connection, transaction, id);

            if (record is null)
            {
                transaction.Commit();
                return;
            }

            switch (state)
            {
                case DeliveryState.Pending:
                    record.FailedFor.RemoveAll(peer => peer == peerId);
                    if (!record.DeliveredTo.Contains(peerId) && !record.PendingFor.Contains(peerId))
                        record.PendingFor.Add(peerId);
                    break;

                case DeliveryState.Delivered:
                    record.PendingFor.RemoveAll(peer => peer == peerId);
                    record.FailedFor.RemoveAll(peer => peer == peerId);
                    if (!record.DeliveredTo.Contains(peerId))
                        record.DeliveredTo.Add(peerId);
                    break;

                case DeliveryState.Failed:
                    record.PendingFor.RemoveAll(peer => peer == peerId);
                    if (!record.FailedFor.Contains(peerId))
                        record.FailedFor.Add(peerId);
                    break;
            }

            await UpdateAsync(connection, transaction, record);
            transaction.Commit();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task TrimAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var limit = Math.Clamp(SettingsService.Instance.Current.HistoryLimit, 10, 5000);
        var removed = new List<(string Id, string? DataFile)>();

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Id, DataFile FROM Items ORDER BY At DESC LIMIT -1 OFFSET $limit";
            select.Parameters.AddWithValue("$limit", limit);

            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                removed.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        foreach (var (id, dataFile) in removed)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Items WHERE Id = $id";
            delete.Parameters.AddWithValue("$id", id);
            await delete.ExecuteNonQueryAsync();

            await DeleteBinaryAsync(dataFile);
            await ClipboardFiles.DeleteViewAsync(id);
            _progress.TryRemove(id, out _);
        }

        if (removed.Count > 0)
            await BlobStore.GarbageCollectAsync(await ReadHashesAsync(connection, transaction));
    }

    private async Task MigrateLegacyAsync(SqliteConnection connection)
    {
        if (!File.Exists(LegacyPath))
            return;

        try
        {
            await using (var count = connection.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Items";
                if (Convert.ToInt64(await count.ExecuteScalarAsync()) > 0)
                {
                    File.Move(LegacyPath, LegacyPath + ".migrated", true);
                    return;
                }
            }

            var json = await File.ReadAllTextAsync(LegacyPath);
            var stored = JsonSerializer.Deserialize(json, AppJsonContext.Default.ClipboardRecordArray) ?? [];

            await _writeGate.WaitAsync();
            try
            {
                using var transaction = connection.BeginTransaction();

                foreach (var record in stored)
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText =
                        """
                        INSERT OR IGNORE INTO Items
                            (Id, OriginDeviceId, OriginDeviceName, Kind, Content, Meta, DataFile, Hash, At,
                             TotalBytes, Progress, Files, Roots, PendingFor, DeliveredTo, FailedFor)
                        VALUES
                            ($id, $originId, $originName, $kind, $content, $meta, $dataFile, $hash, $at,
                             $totalBytes, $progress, $files, $roots, $pending, $delivered, $failed);
                        """;
                    AddRecordParameters(insert, record);
                    await insert.ExecuteNonQueryAsync();
                }

                transaction.Commit();
            }
            finally
            {
                _writeGate.Release();
            }

            File.Move(LegacyPath, LegacyPath + ".migrated", true);
            Constants.Logger.Information("Migrated {Count} clipboard items from history.json", stored.Length);
        }
        catch (Exception ex)
        {
            Constants.Logger.Warning(ex, "Failed to migrate history.json");
        }
    }

    private async Task CleanupAsync(SqliteConnection connection)
    {
        var localId = DeviceIdentity.Instance.Id;
        var items = new List<ClipboardRecord>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {SelectColumns} FROM Items";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                items.Add(ReadRecord(reader));
        }

        var incomplete = items
            .Where(item => item.Kind == "file" &&
                           item.OriginDeviceId != localId &&
                           item.Progress < item.TotalBytes)
            .ToArray();

        if (incomplete.Length > 0)
        {
            await _writeGate.WaitAsync();
            try
            {
                using var transaction = connection.BeginTransaction();

                foreach (var item in incomplete)
                {
                    await using var delete = connection.CreateCommand();
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM Items WHERE Id = $id";
                    delete.Parameters.AddWithValue("$id", item.Id);
                    await delete.ExecuteNonQueryAsync();

                    await DeleteRecordFilesAsync(item);
                    items.Remove(item);
                }

                transaction.Commit();
            }
            finally
            {
                _writeGate.Release();
            }
        }

        var ids = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var views = Path.Combine(Constants.DataDirectory, "clipboard", "items");

        if (Directory.Exists(views))
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(views))
                    if (!ids.Contains(Path.GetFileName(directory)))
                        await ClipboardFiles.DeleteViewAsync(Path.GetFileName(directory));
            }
            catch (Exception ex)
            {
                Constants.Logger.Debug(ex, "Failed to clean up clipboard views");
            }

        await BlobStore.GarbageCollectAsync(items.SelectMany(item => item.Files ?? [])
            .Where(entry => !entry.Directory && entry.Hash.Length == 64)
            .Select(entry => entry.Hash));
    }

    private async Task<IEnumerable<string>> ReadHashesAsync()
    {
        await using var connection = await OpenAsync();
        return await ReadHashesAsync(connection, null);
    }

    private static async Task<IEnumerable<string>> ReadHashesAsync(SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Files FROM Items WHERE Files IS NOT NULL";

        var hashes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var files = JsonSerializer.Deserialize(reader.GetString(0), AppJsonContext.Default.ClipboardFileEntryArray);
            if (files is null)
                continue;

            hashes.AddRange(files.Where(entry => !entry.Directory && entry.Hash.Length == 64)
                .Select(entry => entry.Hash));
        }

        return hashes;
    }

    private static async Task<ClipboardRecord?> ReadOneAsync(SqliteConnection connection,
        SqliteTransaction transaction, string id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SelectColumns} FROM Items WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadRecord(reader) : null;
    }

    private static ClipboardRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        OriginDeviceId = reader.GetString(1),
        OriginDeviceName = reader.GetString(2),
        Kind = reader.GetString(3),
        Content = reader.GetString(4),
        Meta = reader.IsDBNull(5) ? null : reader.GetString(5),
        DataFile = reader.IsDBNull(6) ? null : reader.GetString(6),
        Hash = reader.IsDBNull(7) ? null : reader.GetString(7),
        At = reader.GetInt64(8),
        TotalBytes = reader.GetInt64(9),
        Progress = reader.GetInt64(10),
        Files = ParseFiles(reader.IsDBNull(11) ? null : reader.GetString(11)),
        Roots = ParseStrings(reader.IsDBNull(12) ? null : reader.GetString(12)),
        PendingFor = ParseStrings(reader.IsDBNull(13) ? null : reader.GetString(13)) ?? [],
        DeliveredTo = ParseStrings(reader.IsDBNull(14) ? null : reader.GetString(14)) ?? [],
        FailedFor = ParseStrings(reader.IsDBNull(15) ? null : reader.GetString(15)) ?? []
    };

    private ClipboardRecord OverlayProgress(ClipboardRecord record)
    {
        if (_progress.TryGetValue(record.Id, out var progress))
            record.Progress = progress;

        return record;
    }

    private static async Task UpdateAsync(SqliteConnection connection, SqliteTransaction transaction,
        ClipboardRecord record)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Items SET
                OriginDeviceId = $originId, OriginDeviceName = $originName, Kind = $kind,
                Content = $content, Meta = $meta, DataFile = $dataFile, Hash = $hash, At = $at,
                TotalBytes = $totalBytes, Progress = $progress, Files = $files, Roots = $roots,
                PendingFor = $pending, DeliveredTo = $delivered, FailedFor = $failed
            WHERE Id = $id;
            """;
        AddRecordParameters(command, record);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddRecordParameters(SqliteCommand command, ClipboardRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$originId", record.OriginDeviceId);
        command.Parameters.AddWithValue("$originName", record.OriginDeviceName);
        command.Parameters.AddWithValue("$kind", record.Kind);
        command.Parameters.AddWithValue("$content", record.Content);
        command.Parameters.AddWithValue("$meta", (object?)record.Meta ?? DBNull.Value);
        command.Parameters.AddWithValue("$dataFile", (object?)record.DataFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", (object?)record.Hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", record.At);
        command.Parameters.AddWithValue("$totalBytes", record.TotalBytes);
        command.Parameters.AddWithValue("$progress", record.Progress);
        command.Parameters.AddWithValue("$files", JsonFiles(record.Files) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$roots", JsonStrings(record.Roots) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$pending", JsonStrings(record.PendingFor) ?? "[]");
        command.Parameters.AddWithValue("$delivered", JsonStrings(record.DeliveredTo) ?? "[]");
        command.Parameters.AddWithValue("$failed", JsonStrings(record.FailedFor) ?? "[]");
    }

    private static string? JsonFiles(List<ClipboardFileEntry>? files) =>
        files is null ? null : JsonSerializer.Serialize(files.ToArray(), AppJsonContext.Default.ClipboardFileEntryArray);

    private static string? JsonStrings(List<string>? values) =>
        values is null ? null : JsonSerializer.Serialize(values, AppJsonContext.Default.ListString);

    private static List<ClipboardFileEntry>? ParseFiles(string? json) =>
        json is null ? null : JsonSerializer.Deserialize(json, AppJsonContext.Default.ClipboardFileEntryArray)?.ToList();

    private static List<string>? ParseStrings(string? json) =>
        json is null ? null : JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString);

    public static async Task<string> SaveBinaryAsync(string id, byte[] data, string extension)
    {
        var directory = Path.Combine(Constants.DataDirectory, "clipboard");
        Directory.CreateDirectory(directory);

        var name = id + extension;
        await File.WriteAllBytesAsync(Path.Combine(directory, name), data);
        return Path.Combine("clipboard", name);
    }

    public static async Task<byte[]?> ReadBinaryAsync(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;

        var path = Path.Combine(Constants.DataDirectory, relativePath);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    private static async Task DeleteRecordFilesAsync(ClipboardRecord record)
    {
        await DeleteBinaryAsync(record.DataFile);
        await ClipboardFiles.DeleteViewAsync(record.Id);
    }

    private static Task DeleteBinaryAsync(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            Try.Run(() =>
            {
                var path = Path.Combine(Constants.DataDirectory, relativePath);
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else if (File.Exists(path))
                    File.Delete(path);
            }).OnFailure(ex => Constants.Logger.Debug(ex, "Failed to delete {Path}", relativePath));
        });
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync();

        return connection;
    }

    private enum DeliveryState
    {
        Pending,
        Delivered,
        Failed
    }
}