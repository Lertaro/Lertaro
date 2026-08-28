using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Helper responsible for SQLite table creation, PRAGMA configuration, and schema migrations.
/// Split out to keep database management classes under the repository per-file line limit.
/// </summary>
public static class DatabaseSchemaHelper
{
    public static void InitializeSchema(SqliteConnection conn)
    {
        SqliteVecLoader.TryEnableSqliteVec(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT UNIQUE NOT NULL,
                last_modified INTEGER NOT NULL,
                file_size INTEGER NOT NULL,
                chunk_count INTEGER NOT NULL,
                indexed_at INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);

            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                chunk_index INTEGER NOT NULL,
                offset INTEGER NOT NULL,
                length INTEGER NOT NULL,
                content TEXT NOT NULL,
                FOREIGN KEY(file_id) REFERENCES files(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_file_id ON chunks(file_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                content,
                chunk_id UNINDEXED,
                file_id UNINDEXED,
                tokenize = 'unicode61'
            );
            """;
        cmd.ExecuteNonQuery();
    }
}
