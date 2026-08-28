using Lertaro.Plugins.ContentSearch.Indexing;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

public readonly record struct FileIndexBatchItem(
    string Path,
    DateTime LastModifiedUtc,
    long FileSize,
    IReadOnlyList<TextChunk> Chunks
);

/// <summary>
/// Handles atomic insertions, chunk batch writing, and file deletions with prepared statement reuse.
/// </summary>
public static class DatabaseWriterHelper
{
    public static void InsertOrUpdateBatch(SqliteConnection conn, IReadOnlyList<FileIndexBatchItem> items)
    {
        if (items.Count == 0) return;

        using var tx = conn.BeginTransaction();
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var findCmd = conn.CreateCommand();
        findCmd.Transaction = tx;
        findCmd.CommandText = "SELECT id FROM files WHERE path = @path LIMIT 1;";
        var pFindPath = findCmd.Parameters.Add("@path", SqliteType.Text);
        findCmd.Prepare();

        using var delFtsCmd = conn.CreateCommand();
        delFtsCmd.Transaction = tx;
        delFtsCmd.CommandText = "DELETE FROM chunks_fts WHERE file_id = @file_id;";
        var pDelFtsFileId = delFtsCmd.Parameters.Add("@file_id", SqliteType.Integer);
        delFtsCmd.Prepare();

        using var delFileCmd = conn.CreateCommand();
        delFileCmd.Transaction = tx;
        delFileCmd.CommandText = "DELETE FROM files WHERE id = @file_id;";
        var pDelFileId = delFileCmd.Parameters.Add("@file_id", SqliteType.Integer);
        delFileCmd.Prepare();

        using var insertFileCmd = conn.CreateCommand();
        insertFileCmd.Transaction = tx;
        insertFileCmd.CommandText = """
            INSERT INTO files (path, last_modified, file_size, chunk_count, indexed_at)
            VALUES (@path, @last_modified, @file_size, @chunk_count, @indexed_at);
            SELECT last_insert_rowid();
            """;
        var pPath = insertFileCmd.Parameters.Add("@path", SqliteType.Text);
        var pLastMod = insertFileCmd.Parameters.Add("@last_modified", SqliteType.Integer);
        var pSize = insertFileCmd.Parameters.Add("@file_size", SqliteType.Integer);
        var pChunksCount = insertFileCmd.Parameters.Add("@chunk_count", SqliteType.Integer);
        var pIndexedAt = insertFileCmd.Parameters.Add("@indexed_at", SqliteType.Integer);
        insertFileCmd.Prepare();

        using var insertChunkCmd = conn.CreateCommand();
        insertChunkCmd.Transaction = tx;
        insertChunkCmd.CommandText = """
            INSERT INTO chunks (file_id, chunk_index, offset, length, content)
            VALUES (@file_id, @chunk_index, @offset, @length, @content);
            SELECT last_insert_rowid();
            """;
        var pChunkFileId = insertChunkCmd.Parameters.Add("@file_id", SqliteType.Integer);
        var pChunkIndex = insertChunkCmd.Parameters.Add("@chunk_index", SqliteType.Integer);
        var pChunkOffset = insertChunkCmd.Parameters.Add("@offset", SqliteType.Integer);
        var pChunkLength = insertChunkCmd.Parameters.Add("@length", SqliteType.Integer);
        var pChunkContent = insertChunkCmd.Parameters.Add("@content", SqliteType.Text);
        insertChunkCmd.Prepare();

        using var insertFtsCmd = conn.CreateCommand();
        insertFtsCmd.Transaction = tx;
        insertFtsCmd.CommandText = """
            INSERT INTO chunks_fts (content, chunk_id, file_id)
            VALUES (@content, @chunk_id, @file_id);
            """;
        var pFtsContent = insertFtsCmd.Parameters.Add("@content", SqliteType.Text);
        var pFtsChunkId = insertFtsCmd.Parameters.Add("@chunk_id", SqliteType.Integer);
        var pFtsFileId = insertFtsCmd.Parameters.Add("@file_id", SqliteType.Integer);
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

                pDelFileId.Value = fileId;
                delFileCmd.ExecuteNonQuery();
            }

            if (item.Chunks.Count == 0) continue;

            var lastModUnix = new DateTimeOffset(item.LastModifiedUtc).ToUnixTimeSeconds();
            pPath.Value = item.Path;
            pLastMod.Value = lastModUnix;
            pSize.Value = item.FileSize;
            pChunksCount.Value = item.Chunks.Count;
            pIndexedAt.Value = nowUtc;

            var newFileId = (long)(insertFileCmd.ExecuteScalar() ?? 0L);

            foreach (var chunk in item.Chunks)
            {
                pChunkFileId.Value = newFileId;
                pChunkIndex.Value = chunk.ChunkIndex;
                pChunkOffset.Value = chunk.Offset;
                pChunkLength.Value = chunk.Length;
                pChunkContent.Value = chunk.Text;
                var chunkId = (long)(insertChunkCmd.ExecuteScalar() ?? 0L);

                pFtsContent.Value = chunk.Text;
                pFtsChunkId.Value = chunkId;
                pFtsFileId.Value = newFileId;
                insertFtsCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    public static void InsertOrUpdateFile(SqliteConnection conn, string path, DateTime lastModifiedUtc, long fileSize, IReadOnlyList<TextChunk> chunks) =>
        InsertOrUpdateBatch(conn, new[] { new FileIndexBatchItem(path, lastModifiedUtc, fileSize, chunks) });

    public static void DeleteFile(SqliteConnection conn, string path)
    {
        using var tx = conn.BeginTransaction();
        DeleteFileInternal(conn, path, tx);
        tx.Commit();
    }

    public static void DeleteFilesBatch(SqliteConnection conn, IEnumerable<string> paths)
    {
        using var tx = conn.BeginTransaction();
        foreach (var path in paths)
        {
            DeleteFileInternal(conn, path, tx);
        }
        tx.Commit();
    }

    public static void DeleteFileInternal(SqliteConnection conn, string path, SqliteTransaction tx)
    {
        long? fileId = null;
        using (var findCmd = conn.CreateCommand())
        {
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT id FROM files WHERE path = @path LIMIT 1;";
            findCmd.Parameters.AddWithValue("@path", path);
            var res = findCmd.ExecuteScalar();
            if (res != null && res != DBNull.Value)
                fileId = (long)res;
        }

        if (!fileId.HasValue) return;

        using (var delFtsCmd = conn.CreateCommand())
        {
            delFtsCmd.Transaction = tx;
            delFtsCmd.CommandText = "DELETE FROM chunks_fts WHERE file_id = @file_id;";
            delFtsCmd.Parameters.AddWithValue("@file_id", fileId.Value);
            delFtsCmd.ExecuteNonQuery();
        }

        using var delFileCmd = conn.CreateCommand();
        delFileCmd.Transaction = tx;
        delFileCmd.CommandText = "DELETE FROM files WHERE id = @file_id;";
        delFileCmd.Parameters.AddWithValue("@file_id", fileId.Value);
        delFileCmd.ExecuteNonQuery();
    }
}
