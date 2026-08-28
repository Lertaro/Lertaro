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
/// Handles atomic insertions, chunk batch writing, and file deletions within database transactions.
/// </summary>
public static class DatabaseWriterHelper
{
    public static void InsertOrUpdateBatch(SqliteConnection conn, IReadOnlyList<FileIndexBatchItem> items)
    {
        if (items.Count == 0) return;

        using var tx = conn.BeginTransaction();
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var item in items)
        {
            DeleteFileInternal(conn, item.Path, tx);
            if (item.Chunks.Count == 0) continue;

            var lastModUnix = new DateTimeOffset(item.LastModifiedUtc).ToUnixTimeSeconds();
            long fileId;
            using (var insertFileCmd = conn.CreateCommand())
            {
                insertFileCmd.Transaction = tx;
                insertFileCmd.CommandText = """
                    INSERT INTO files (path, last_modified, file_size, chunk_count, indexed_at)
                    VALUES (@path, @last_modified, @file_size, @chunk_count, @indexed_at);
                    SELECT last_insert_rowid();
                    """;
                insertFileCmd.Parameters.AddWithValue("@path", item.Path);
                insertFileCmd.Parameters.AddWithValue("@last_modified", lastModUnix);
                insertFileCmd.Parameters.AddWithValue("@file_size", item.FileSize);
                insertFileCmd.Parameters.AddWithValue("@chunk_count", item.Chunks.Count);
                insertFileCmd.Parameters.AddWithValue("@indexed_at", nowUtc);
                fileId = (long)(insertFileCmd.ExecuteScalar() ?? 0L);
            }

            foreach (var chunk in item.Chunks)
            {
                long chunkId;
                using (var insertChunkCmd = conn.CreateCommand())
                {
                    insertChunkCmd.Transaction = tx;
                    insertChunkCmd.CommandText = """
                        INSERT INTO chunks (file_id, chunk_index, offset, length, content)
                        VALUES (@file_id, @chunk_index, @offset, @length, @content);
                        SELECT last_insert_rowid();
                        """;
                    insertChunkCmd.Parameters.AddWithValue("@file_id", fileId);
                    insertChunkCmd.Parameters.AddWithValue("@chunk_index", chunk.ChunkIndex);
                    insertChunkCmd.Parameters.AddWithValue("@offset", chunk.Offset);
                    insertChunkCmd.Parameters.AddWithValue("@length", chunk.Length);
                    insertChunkCmd.Parameters.AddWithValue("@content", chunk.Text);
                    chunkId = (long)(insertChunkCmd.ExecuteScalar() ?? 0L);
                }

                using var insertFtsCmd = conn.CreateCommand();
                insertFtsCmd.Transaction = tx;
                insertFtsCmd.CommandText = """
                        INSERT INTO chunks_fts (content, chunk_id, file_id)
                        VALUES (@content, @chunk_id, @file_id);
                        """;
                insertFtsCmd.Parameters.AddWithValue("@content", chunk.Text);
                insertFtsCmd.Parameters.AddWithValue("@chunk_id", chunkId);
                insertFtsCmd.Parameters.AddWithValue("@file_id", fileId);
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
