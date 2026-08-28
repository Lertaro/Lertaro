using Lertaro.Plugins.ContentSearch.Indexing;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Manages SQLite storage, FTS5 full-text indexing, and search queries for document chunks.
/// </summary>
public sealed class ContentSearchDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();
    private bool _initialized;

    public ContentSearchDatabase(string dbPath)
    {
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
        lock (_lock)
        {
            if (_initialized) return;

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseSchemaHelper.InitializeSchema(conn);
            _initialized = true;
        }
    }

    public void InsertOrUpdateBatch(IReadOnlyList<FileIndexBatchItem> items)
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.InsertOrUpdateBatch(conn, items);
        }
    }

    public void InsertOrUpdateFile(string path, DateTime lastModifiedUtc, long fileSize, IReadOnlyList<TextChunk> chunks)
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.InsertOrUpdateFile(conn, path, lastModifiedUtc, fileSize, chunks);
        }
    }

    public void DeleteFile(string path)
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.DeleteFile(conn, path);
        }
    }

    public void DeleteFilesBatch(IEnumerable<string> paths)
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            DatabaseWriterHelper.DeleteFilesBatch(conn, paths);
        }
    }

    public Dictionary<string, (long LastModified, long FileSize)> GetAllFileMetadata()
    {
        Initialize();
        lock (_lock)
        {
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
    }

    public IndexedFileRecord? GetFileRecord(string path)
    {
        Initialize();
        lock (_lock)
        {
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
    }

    public HashSet<string> GetAllIndexedPaths()
    {
        Initialize();
        lock (_lock)
        {
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
    }

    public IReadOnlyList<SearchHitItem> SearchFts(string rawQuery, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
            return Array.Empty<SearchHitItem>();

        Initialize();
        var ftsQuery = DatabaseFtsQueryHelper.BuildFtsQuery(rawQuery);

        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return DatabaseSearchHelper.Search(conn, rawQuery, ftsQuery, limit);
        }
    }

    public (int TotalFiles, int TotalChunks) GetStats()
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT (SELECT COUNT(*) FROM files), (SELECT COUNT(*) FROM chunks);";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetInt32(0), reader.GetInt32(1));
            }
            return (0, 0);
        }
    }

    public void ClearAll()
    {
        Initialize();
        lock (_lock)
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                DELETE FROM chunks_fts;
                DELETE FROM chunks;
                DELETE FROM files;
                VACUUM;
                """;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
    }
}
