using Lertaro.Plugins.ContentSearch.Indexing;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Manages SQLite storage, FTS5 full-text indexing, and search queries for documents.
/// </summary>
public sealed class ContentSearchDatabase : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _writeLock = new();
    private bool _initialized;

    private int _cachedTotalFiles;

    public int TotalFiles => _cachedTotalFiles;
    public int TotalChunks => _cachedTotalFiles;

    public ContentSearchDatabase(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ToString();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Initialize()
    {
        if (_initialized) return;
        lock (_writeLock)
        {
            if (_initialized) return;

            using var conn = OpenConnection();
            DatabaseSchemaHelper.InitializeSchema(conn);
            RefreshStatsInternal(conn);
            _initialized = true;
        }
    }

    public IReadOnlyDictionary<string, long> InsertOrUpdateBatch(IReadOnlyList<FileIndexBatchItem> items)
    {
        Initialize();
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            var result = DatabaseWriterHelper.InsertOrUpdateBatch(conn, items);
            RefreshStatsInternal(conn);
            return result;
        }
    }

    public void InsertOrUpdateFile(string path, DateTime lastModifiedUtc, long fileSize, string content)
    {
        Initialize();
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            DatabaseWriterHelper.InsertOrUpdateFile(conn, path, lastModifiedUtc, fileSize, content);
            RefreshStatsInternal(conn);
        }
    }

    public void DeleteFile(string path)
    {
        if (!File.Exists(_dbPath)) return;
        Initialize();
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            DatabaseWriterHelper.DeleteFile(conn, path);
            RefreshStatsInternal(conn);
        }
    }

    public void DeleteFilesBatch(IEnumerable<string> paths)
    {
        if (!File.Exists(_dbPath)) return;
        Initialize();
        lock (_writeLock)
        {
            using var conn = OpenConnection();
            DatabaseWriterHelper.DeleteFilesBatch(conn, paths);
            RefreshStatsInternal(conn);
        }
    }

    public void Checkpoint(bool truncate = false)
    {
        if (!File.Exists(_dbPath)) return;
        lock (_writeLock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = truncate ? "PRAGMA wal_checkpoint(TRUNCATE);" : "PRAGMA wal_checkpoint(PASSIVE);";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public void Optimize()
    {
        if (!File.Exists(_dbPath)) return;
        lock (_writeLock)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO files_fts(files_fts) VALUES('optimize');
                    PRAGMA wal_checkpoint(TRUNCATE);
                    """;
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public Dictionary<string, (long LastModified, long FileSize)> GetAllFileMetadata()
    {
        if (!File.Exists(_dbPath)) return new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        var dict = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path, last_modified, file_size FROM files;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dict[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
        }
        return dict;
    }

    public IndexedFileRecord? GetFileRecord(string path)
    {
        if (!File.Exists(_dbPath)) return null;
        Initialize();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, last_modified, file_size, indexed_at, failed_at, content_hash, content_ref FROM files WHERE path = @path LIMIT 1;";
        cmd.Parameters.AddWithValue("@path", path);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return new IndexedFileRecord
            {
                Id = reader.GetInt64(0),
                Path = reader.GetString(1),
                LastModified = reader.GetInt64(2),
                FileSize = reader.GetInt64(3),
                IndexedAt = reader.GetInt64(4),
                FailedAt = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                ContentHash = reader.IsDBNull(6) ? null : reader.GetString(6),
                ContentRef = reader.IsDBNull(7) ? null : reader.GetInt64(7)
            };
        }
        return null;
    }

    /// <summary>
    /// Finds the id of an indexed row holding this exact content: a source row (its own
    /// text, not failed, not itself a duplicate) other than the given path.
    /// </summary>
    public long? FindIndexedSourceByHash(string contentHash, string selfPath)
    {
        if (!File.Exists(_dbPath)) return null;
        Initialize();

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id FROM files
            WHERE content_hash = @hash AND content_ref IS NULL AND failed_at IS NULL AND path <> @self
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@hash", contentHash);
        cmd.Parameters.AddWithValue("@self", selfPath);
        var res = cmd.ExecuteScalar();
        return res != null && res != DBNull.Value ? (long)res : null;
    }

    public HashSet<string> GetAllIndexedPaths()
    {
        if (!File.Exists(_dbPath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path FROM files;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }
        return paths;
    }

    public IReadOnlyList<SearchHitItem> SearchFts(string rawQuery, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(rawQuery) || !File.Exists(_dbPath))
            return Array.Empty<SearchHitItem>();

        Initialize();
        var ftsQuery = DatabaseFtsQueryHelper.BuildFtsQuery(rawQuery);

        using var conn = OpenConnection();
        return DatabaseSearchHelper.Search(conn, rawQuery, ftsQuery, limit);
    }

    public (int TotalFiles, int TotalChunks) GetStats()
    {
        if (!File.Exists(_dbPath)) return (0, 0);
        Initialize();
        return (_cachedTotalFiles, _cachedTotalFiles);
    }

    private void RefreshStatsInternal(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM files;";
            var res = cmd.ExecuteScalar();
            _cachedTotalFiles = res != null && res != DBNull.Value ? Convert.ToInt32(res) : 0;
        }
        catch { }
    }

    public void ClearAll()
    {
        lock (_writeLock)
        {
            _cachedTotalFiles = 0;

            if (!File.Exists(_dbPath)) return;

            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    DELETE FROM files_fts;
                    DELETE FROM files;
                    VACUUM;
                    PRAGMA wal_checkpoint(TRUNCATE);
                    """;
                cmd.ExecuteNonQuery();
            }
            catch
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    TryDeleteFile(_dbPath);
                    TryDeleteFile(_dbPath + "-wal");
                    TryDeleteFile(_dbPath + "-shm");
                }
                catch { }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void Dispose() => SqliteConnection.ClearAllPools();
}
