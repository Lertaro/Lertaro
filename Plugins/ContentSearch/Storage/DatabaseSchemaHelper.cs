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

        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = """
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
                """;
            pragmaCmd.ExecuteNonQuery();
        }

        EnsureTrigramFtsSchema(conn);
    }

    private static void EnsureTrigramFtsSchema(SqliteConnection conn)
    {
        var needsMigration = false;
        using (var checkCmd = conn.CreateCommand())
        {
            checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='chunks_fts';";
            if (checkCmd.ExecuteScalar() is not string sql || !sql.Contains("trigram", StringComparison.OrdinalIgnoreCase))
            {
                needsMigration = true;
            }
        }

        if (!needsMigration) return;

        using var migrateCmd = conn.CreateCommand();
        migrateCmd.CommandText = """
            DROP TABLE IF EXISTS chunks_fts;
            CREATE VIRTUAL TABLE chunks_fts USING fts5(
                content,
                chunk_id UNINDEXED,
                file_id UNINDEXED,
                tokenize = 'trigram'
            );
            INSERT INTO chunks_fts (content, chunk_id, file_id)
            SELECT content, id, file_id FROM chunks;
            """;
        migrateCmd.ExecuteNonQuery();
    }
}
