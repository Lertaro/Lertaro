using Lertaro.Plugins.ContentSearch.Indexing;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Manages SQLite storage, FTS5 full-text indexing, and search queries for document chunks.
/// </summary>
public sealed class ContentSearchDatabase : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly object _writeLock = new();
    private bool _initialized;

    private int _cachedTotalFiles;
    private int _cachedTotalChunks;

    public int TotalFiles => _cachedTotalFiles;
    public int TotalChunks => _cachedTotalChunks;

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

    public void Initialize()
    {
        if (_initialized) return;
        lock (_writeLock)
        {
            if (_initialized) return;

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseSchemaHelper.InitializeSchema(conn);
            RefreshStatsInternal(conn);
            _initialized = true;
        }
    }

    public void InsertOrUpdateBatch(IReadOnlyList<FileIndexBatchItem> items)
    {
        Initialize();
        lock (_writeLock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.InsertOrUpdateBatch(conn, items);
            RefreshStatsInternal(conn);
        }
    }

    public void InsertOrUpdateFile(string path, DateTime lastModifiedUtc, long fileSize, IReadOnlyList<TextChunk> chunks)
    {
        Initialize();
        lock (_writeLock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.InsertOrUpdateFile(conn, path, lastModifiedUtc, fileSize, chunks);
            RefreshStatsInternal(conn);
        }
    }

    public void DeleteFile(string path)
    {
        if (!File.Exists(_dbPath)) return;
        Initialize();
        lock (_writeLock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
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
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.DeleteFilesBatch(conn, paths);
            RefreshStatsInternal(conn);
        }
    }

    public Dictionary<string, (long LastModified, long FileSize)> GetAllFileMetadata()
    {
        if (!File.Exists(_dbPath)) return new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        var dict = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
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

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, last_modified, file_size, chunk_count, indexed_at FROM files WHERE path = @path LIMIT 1;";
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
                ChunkCount = reader.GetInt32(4),
                IndexedAt = reader.GetInt64(5)
            };
        }
        return null;
    }

    public HashSet<string> GetAllIndexedPaths()
    {
        if (!File.Exists(_dbPath)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Initialize();

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
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

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return DatabaseSearchHelper.Search(conn, rawQuery, ftsQuery, limit);
    }

    public (int TotalFiles, int TotalChunks) GetStats()
    {
        if (!File.Exists(_dbPath)) return (0, 0);
        Initialize();
        return (_cachedTotalFiles, _cachedTotalChunks);
    }

    private void RefreshStatsInternal(SqliteConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM files), (SELECT COUNT(*) FROM chunks);";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                _cachedTotalFiles = reader.GetInt32(0);
                _cachedTotalChunks = reader.GetInt32(1);
            }
        }
        catch { }
    }

    public void ClearAll()
    {
        lock (_writeLock)
        {
            _initialized = false;
            _cachedTotalFiles = 0;
            _cachedTotalChunks = 0;

            SqliteConnection.ClearAllPools();
            TryDeleteFile(_dbPath);
            TryDeleteFile(_dbPath + "-wal");
            TryDeleteFile(_dbPath + "-shm");
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
