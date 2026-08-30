using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Helper responsible for SQLite table creation, PRAGMA configuration, and standard FTS5 schema initialization.
/// </summary>
public static class DatabaseSchemaHelper
{
    public static void InitializeSchema(SqliteConnection conn)
    {
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA wal_autocheckpoint = 1000;
                """;
            pragmaCmd.ExecuteNonQuery();
        }

        using var tableCmd = conn.CreateCommand();
        // ponytail: content_hash and content_ref carry no index by design; both back
        // dedup lookups and cascade deletions that scan the whole files table, which is
        // fine at the current corpus size (see FindIndexedSourceByHash's tripwire
        // warning). If the corpus outgrows it, add:
        // CREATE INDEX idx_files_content_hash ON files(content_hash);
        //
        // Full-text indexing lives in Lucene (see LuceneContentIndex) on this branch: the
        // FTS5 trigram table is gone, so text is never stored or searched here.
        tableCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT UNIQUE NOT NULL,
                    last_modified INTEGER NOT NULL,
                    file_size INTEGER NOT NULL,
                    indexed_at INTEGER NOT NULL,
                    failed_at INTEGER,
                    content_hash TEXT,
                    content_ref INTEGER
                );

                CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);
                """;
        tableCmd.ExecuteNonQuery();

        AddColumnIfMissing(conn, "files", "failed_at", "INTEGER");
        AddColumnIfMissing(conn, "files", "content_hash", "TEXT");
        AddColumnIfMissing(conn, "files", "content_ref", "INTEGER");
    }

    /// <summary>
    /// Adds a column to an existing table when the database predates it. The fresh-table
    /// CREATE above already includes newer columns, so this only fires on older databases.
    /// </summary>
    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string type)
    {
        var hasColumn = false;
        using (var infoCmd = conn.CreateCommand())
        {
            infoCmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = infoCmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    hasColumn = true;
                    break;
                }
            }
        }

        if (hasColumn) return;

        using var alterCmd = conn.CreateCommand();
        alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
        alterCmd.ExecuteNonQuery();
    }
}
