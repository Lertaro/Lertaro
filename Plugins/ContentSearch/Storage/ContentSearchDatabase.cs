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

    /// <summary>
    /// Runs VACUUM when a large share of the database is free pages left over from
    /// deleted rows, reclaiming the file space. Cheap no-op on a compact database.
    /// </summary>
    public void VacuumIfBloat(double maxFreeRatio = 0.3)
    {
        if (!File.Exists(_dbPath)) return;
        lock (_writeLock)
        {
            try
            {
                using var conn = OpenConnection();
                DatabaseMaintenanceHelper.VacuumIfBloat(conn, maxFreeRatio);
            }
            catch { }
        }
    }

    /// <summary>
    /// Total on-disk footprint of the index (all database pages, free ones included).
    /// </summary>
    public long GetDatabasePageBytes()
    {
        if (!File.Exists(_dbPath)) return 0;
        try
        {
            using var conn = OpenConnection();
            return DatabaseMaintenanceHelper.GetDatabasePageBytes(conn);
        }
        catch { return 0; }
    }

    public Dictionary<string, (long LastModified, long FileSize)> GetAllFileMetadata()
    {
        if (!File.Exists(_dbPath)) return new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        using var conn = OpenConnection();
        return DatabaseMetadataReader.GetAllFileMetadata(conn);
    }

    public IndexedFileRecord? GetFileRecord(string path)
    {
        if (!File.Exists(_dbPath)) return null;
        Initialize();

        using var conn = OpenConnection();
        return DatabaseMetadataReader.GetFileRecord(conn, path);
    }

    public long? FindIndexedSourceByHash(string contentHash, string selfPath)
    {
        if (!File.Exists(_dbPath)) return null;
        Initialize();

        using var conn = OpenConnection();
        return DatabaseMetadataReader.FindIndexedSourceByHash(conn, contentHash, selfPath);
    }

    public HashSet<string> GetAllIndexedPaths()
    {
        if (!File.Exists(_dbPath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        using var conn = OpenConnection();
        return DatabaseMetadataReader.GetAllIndexedPaths(conn);
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
