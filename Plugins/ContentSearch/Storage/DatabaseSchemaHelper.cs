using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Helper responsible for SQLite table creation, PRAGMA configuration, and file-level Contentless FTS5 schema initialization.
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
        tableCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT UNIQUE NOT NULL,
                    last_modified INTEGER NOT NULL,
                    file_size INTEGER NOT NULL,
                    indexed_at INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);

                CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(
                    content,
                    content='',
                    contentless_delete=1,
                    tokenize = 'trigram'
                );
                """;
        tableCmd.ExecuteNonQuery();
    }
}
