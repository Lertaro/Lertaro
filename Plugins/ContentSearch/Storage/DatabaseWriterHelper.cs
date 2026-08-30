using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// A file entering the index: full extracted text for normally indexed files, empty text
/// with <see cref="ContentRef"/> set for duplicates of an already-indexed source, and
/// empty text with no reference for failed extractions.
/// </summary>
/// <param name="ContentHash">Hex XxHash-128 of the file bytes for dedup-eligible files, else null.</param>
/// <param name="ContentRef">Id of the source row whose text this duplicate reuses, else null.</param>
public readonly record struct FileIndexBatchItem(
    string Path,
    DateTime LastModifiedUtc,
    long FileSize,
    string Content,
    string? ContentHash = null,
    long? ContentRef = null
);

/// <summary>
/// Handles atomic file-level insertions, FTS indexing, and deletions.
/// </summary>
public static class DatabaseWriterHelper
{
    /// <summary>
    /// Writes a batch of file rows (and FTS entries for text-bearing, non-duplicate items).
    /// Returns the mapping from path to the row id assigned to each item, which callers
    /// need to wire duplicate rows (content_ref) to their source written in the same call.
    /// </summary>
    public static IReadOnlyDictionary<string, long> InsertOrUpdateBatch(SqliteConnection conn, IReadOnlyList<FileIndexBatchItem> items)
    {
        var idByPath = new Dictionary<string, long>(items.Count, StringComparer.OrdinalIgnoreCase);
        if (items.Count == 0) return idByPath;

        using var tx = conn.BeginTransaction();
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var findCmd = conn.CreateCommand();
        findCmd.Transaction = tx;
        findCmd.CommandText = "SELECT id FROM files WHERE path = @path LIMIT 1;";
        var pFindPath = findCmd.Parameters.Add("@path", SqliteType.Text);
        findCmd.Prepare();

        using var delFtsCmd = conn.CreateCommand();
        delFtsCmd.Transaction = tx;
        delFtsCmd.CommandText = "DELETE FROM files_fts WHERE rowid = @file_id;";
        var pDelFtsFileId = delFtsCmd.Parameters.Add("@file_id", SqliteType.Integer);
        delFtsCmd.Prepare();

        using var delFileCmd = conn.CreateCommand();
        delFileCmd.Transaction = tx;
        delFileCmd.CommandText = "DELETE FROM files WHERE id = @file_id;";
        var pDelFileId = delFileCmd.Parameters.Add("@file_id", SqliteType.Integer);
        delFileCmd.Prepare();

        // Rows referencing this file as their duplicate source must go too: their reuse
        // target is about to change identity (delete+reinsert takes a new id) and its new
        // content may no longer match theirs. They are re-discovered on the next scan.
        using var delRefCmd = conn.CreateCommand();
        delRefCmd.Transaction = tx;
        delRefCmd.CommandText = "DELETE FROM files WHERE content_ref = @file_id;";
        var pDelRefId = delRefCmd.Parameters.Add("@file_id", SqliteType.Integer);
        delRefCmd.Prepare();

        using var insertFileCmd = conn.CreateCommand();
        insertFileCmd.Transaction = tx;
        insertFileCmd.CommandText = """
            INSERT INTO files (path, last_modified, file_size, indexed_at, failed_at, content_hash, content_ref)
            VALUES (@path, @last_modified, @file_size, @indexed_at, @failed_at, @content_hash, @content_ref);
            SELECT last_insert_rowid();
            """;
        var pPath = insertFileCmd.Parameters.Add("@path", SqliteType.Text);
        var pLastMod = insertFileCmd.Parameters.Add("@last_modified", SqliteType.Integer);
        var pSize = insertFileCmd.Parameters.Add("@file_size", SqliteType.Integer);
        var pIndexedAt = insertFileCmd.Parameters.Add("@indexed_at", SqliteType.Integer);
        var pFailedAt = insertFileCmd.Parameters.Add("@failed_at", SqliteType.Integer);
        var pContentHash = insertFileCmd.Parameters.Add("@content_hash", SqliteType.Text);
        var pContentRef = insertFileCmd.Parameters.Add("@content_ref", SqliteType.Integer);
        insertFileCmd.Prepare();

        using var insertFtsCmd = conn.CreateCommand();
        insertFtsCmd.Transaction = tx;
        insertFtsCmd.CommandText = """
            INSERT INTO files_fts (rowid, content)
            VALUES (@rowid, @content);
            """;
        var pFtsRowId = insertFtsCmd.Parameters.Add("@rowid", SqliteType.Integer);
        var pFtsContent = insertFtsCmd.Parameters.Add("@content", SqliteType.Text);
        insertFtsCmd.Prepare();

        foreach (var item in items)
        {
            pFindPath.Value = item.Path;
            var res = findCmd.ExecuteScalar();
            if (res != null && res != DBNull.Value)
            {
                var fileId = (long)res;
                pDelFtsFileId.Value = fileId;
                delFtsCmd.ExecuteNonQuery();

                pDelRefId.Value = fileId;
                delRefCmd.ExecuteNonQuery();

                pDelFileId.Value = fileId;
                delFileCmd.ExecuteNonQuery();
            }

            // An empty-content item without a source reference is a failed extraction (parse
            // error, timeout, binary file, no text layer). The row is kept with its real
            // mtime/size so discovery sees the file as already visited while unchanged, and
            // failed_at marks it as not indexed; the file is retried once mtime/size change.
            // An empty-content item WITH a reference is a duplicate: it reuses the source
            // row's text and owns no FTS entry of its own.
            var failed = string.IsNullOrWhiteSpace(item.Content) && item.ContentRef is null;
            var lastModUnix = new DateTimeOffset(item.LastModifiedUtc).ToUnixTimeSeconds();
            pPath.Value = item.Path;
            pLastMod.Value = lastModUnix;
            pSize.Value = item.FileSize;
            pIndexedAt.Value = nowUtc;
            pFailedAt.Value = failed ? nowUtc : DBNull.Value;
            pContentHash.Value = (object?)item.ContentHash ?? DBNull.Value;
            pContentRef.Value = (object?)item.ContentRef ?? DBNull.Value;

            var newFileId = (long)(insertFileCmd.ExecuteScalar() ?? 0L);
            idByPath[item.Path] = newFileId;

            if (!failed && item.ContentRef is null && !string.IsNullOrWhiteSpace(item.Content))
            {
                pFtsRowId.Value = newFileId;
                pFtsContent.Value = item.Content;
                insertFtsCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
        return idByPath;
    }

    public static IReadOnlyDictionary<string, long> InsertOrUpdateFile(SqliteConnection conn, string path, DateTime lastModifiedUtc, long fileSize, string content) =>
        InsertOrUpdateBatch(conn, new[] { new FileIndexBatchItem(path, lastModifiedUtc, fileSize, content) });

    public static void DeleteFile(SqliteConnection conn, string path) => DeleteFilesBatch(conn, new[] { path });

    public static void DeleteFilesBatch(SqliteConnection conn, IEnumerable<string> paths)
    {
        var pathList = paths as IReadOnlyList<string> ?? paths.ToList();
        if (pathList.Count == 0) return;

        using var tx = conn.BeginTransaction();

        using (var createTemp = conn.CreateCommand())
        {
            createTemp.Transaction = tx;
            createTemp.CommandText = "CREATE TEMP TABLE IF NOT EXISTS to_delete (path TEXT PRIMARY KEY); DELETE FROM to_delete;";
            createTemp.ExecuteNonQuery();
        }

        using (var insertTemp = conn.CreateCommand())
        {
            insertTemp.Transaction = tx;
            insertTemp.CommandText = "INSERT OR IGNORE INTO to_delete (path) VALUES (@path);";
            var pPath = insertTemp.Parameters.Add("@path", SqliteType.Text);
            insertTemp.Prepare();

            foreach (var path in pathList)
            {
                pPath.Value = path;
                insertTemp.ExecuteNonQuery();
            }
        }

        using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = """
                DELETE FROM files WHERE content_ref IN (SELECT id FROM files WHERE path IN (SELECT path FROM to_delete));
                DELETE FROM files_fts WHERE rowid IN (SELECT id FROM files WHERE path IN (SELECT path FROM to_delete));
                DELETE FROM files WHERE path IN (SELECT path FROM to_delete);
                DROP TABLE to_delete;
                """;
            delCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }
}
