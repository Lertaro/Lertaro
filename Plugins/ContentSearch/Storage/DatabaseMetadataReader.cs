using System.Diagnostics;
using Lertaro.Plugins.ContentSearch.Indexing;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Read-only metadata queries over the files table (records, discovery caches, hash
/// lookups). Split out purely to keep ContentSearchDatabase under the repository's
/// per-file line limit; these helpers hold no state and always operate on the one
/// connection passed in per call.
/// </summary>
public static class DatabaseMetadataReader
{
    // Threshold above which a hash lookup (a full files-table scan; no content_hash
    // index by design) warns that the corpus has outgrown the trade-off.
    private const long SlowHashLookupWarnMs = 500;

    public static Dictionary<string, (long LastModified, long FileSize)> GetAllFileMetadata(SqliteConnection conn)
    {
        var dict = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT path, last_modified, file_size FROM files;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            dict[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2));
        }
        return dict;
    }

    public static IndexedFileRecord? GetFileRecord(SqliteConnection conn, string path)
    {
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
    public static long? FindIndexedSourceByHash(SqliteConnection conn, string contentHash, string selfPath)
    {
        // ponytail: no index on content_hash by design: one full files-table scan per
        // dedup-eligible file (>= 10 MB) is acceptable at the current corpus size, and
        // the column only fills for large files. The warning below is the tripwire: if
        // it starts firing regularly, add CREATE INDEX idx_files_content_hash ON
        // files(content_hash) to InitializeSchema.
        var stopwatch = Stopwatch.StartNew();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id FROM files
            WHERE content_hash = @hash AND content_ref IS NULL AND failed_at IS NULL AND path <> @self
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@hash", contentHash);
        cmd.Parameters.AddWithValue("@self", selfPath);
        var res = cmd.ExecuteScalar();

        if (stopwatch.ElapsedMilliseconds >= SlowHashLookupWarnMs)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Content-hash dedup lookup took {stopwatch.ElapsedMilliseconds} ms without a content_hash index; consider indexing files(content_hash)",
                PluginSdk.LogLevel.Warn);
        }

        return res != null && res != DBNull.Value ? (long)res : null;
    }

    public static HashSet<string> GetAllIndexedPaths(SqliteConnection conn)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
