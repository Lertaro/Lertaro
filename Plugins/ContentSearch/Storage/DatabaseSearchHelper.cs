using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text and fallback queries against database tables and resolves snippets on-demand from files.
/// Split out purely to keep ContentSearchDatabase under the repository per-file line limit.
/// </summary>
public static class DatabaseSearchHelper
{
    public static IReadOnlyList<SearchHitItem> Search(SqliteConnection conn, string rawQuery, string ftsQuery, int limit)
    {
        var hits = new List<SearchHitItem>();
        var seenChunkIds = new HashSet<long>();
        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            ExecuteFts(conn, ftsQuery, rawQuery, limit, seenChunkIds, hits);

            if (hits.Count < limit && tokens.Length > 1)
            {
                var compacted = DatabaseFtsQueryHelper.BuildFtsQuery(string.Concat(tokens));
                if (!string.IsNullOrEmpty(compacted) && compacted != ftsQuery)
                {
                    ExecuteFts(conn, compacted, rawQuery, limit, seenChunkIds, hits);
                }
            }
        }

        // Handle short (< 3 chars) queries where Contentless trigram FTS cannot verify match in isolation
        if (hits.Count < limit && tokens.Length > 0 && tokens.All(t => t.Length < 3))
        {
            ScanChunksForShortQuery(conn, tokens, rawQuery, limit, seenChunkIds, hits);
        }

        if (tokens.Length > 0 && hits.Count < limit)
        {
            var remainingLimit = limit - hits.Count;
            using var likeCmd = conn.CreateCommand();
            var whereClauses = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                whereClauses.Add($"f.path LIKE @p{i}");
                likeCmd.Parameters.AddWithValue($"@p{i}", "%" + tokens[i].Trim() + "%");
            }

            likeCmd.CommandText = $"""
                SELECT c.id, c.chunk_index, c.offset, c.length, f.path
                FROM chunks c
                JOIN files f ON f.id = c.file_id
                WHERE {string.Join(" AND ", whereClauses)}
                LIMIT @limit;
                """;
            likeCmd.Parameters.AddWithValue("@limit", remainingLimit);

            using var reader = likeCmd.ExecuteReader();
            while (reader.Read())
            {
                var chunkId = reader.GetInt64(0);
                if (seenChunkIds.Add(chunkId))
                {
                    var chunkIndex = reader.GetInt32(1);
                    var offset = reader.GetInt32(2);
                    var length = reader.GetInt32(3);
                    var filePath = reader.GetString(4);

                    var contextText = SnippetFileHelper.ReadSnippetContext(filePath, offset, length);
                    var snippet = SnippetGenerator.CreateSnippet(contextText, rawQuery);

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        ChunkIndex = chunkIndex,
                        Snippet = snippet,
                        Score = 1.0
                    });
                }
            }
        }

        return hits;
    }

    private static void ScanChunksForShortQuery(
        SqliteConnection conn,
        string[] tokens,
        string rawQuery,
        int limit,
        HashSet<long> seenChunkIds,
        List<SearchHitItem> hits)
    {
        try
        {
            using var chunkScanCmd = conn.CreateCommand();
            chunkScanCmd.CommandText = "SELECT c.id, c.chunk_index, c.offset, c.length, f.path FROM chunks c JOIN files f ON f.id = c.file_id LIMIT 200;";
            using var reader = chunkScanCmd.ExecuteReader();
            while (reader.Read() && hits.Count < limit)
            {
                var chunkId = reader.GetInt64(0);
                if (seenChunkIds.Add(chunkId))
                {
                    var chunkIndex = reader.GetInt32(1);
                    var offset = reader.GetInt32(2);
                    var length = reader.GetInt32(3);
                    var filePath = reader.GetString(4);

                    var contextText = SnippetFileHelper.ReadSnippetContext(filePath, offset, length);
                    if (!string.IsNullOrEmpty(contextText) && tokens.All(t => contextText.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        var snippet = SnippetGenerator.CreateSnippet(contextText, rawQuery);
                        hits.Add(new SearchHitItem
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                            ChunkIndex = chunkIndex,
                            Snippet = snippet,
                            Score = 1.0
                        });
                    }
                }
            }
        }
        catch { }
    }

    private static void ExecuteFts(
        SqliteConnection conn,
        string query,
        string rawQuery,
        int limit,
        HashSet<long> seenChunkIds,
        List<SearchHitItem> hits)
    {
        try
        {
            var remainingLimit = limit - hits.Count;
            if (remainingLimit <= 0) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT c.id, c.chunk_index, c.offset, c.length, f.path, rank
                FROM chunks_fts(@query)
                JOIN chunks c ON c.id = chunks_fts.rowid
                JOIN files f ON f.id = c.file_id
                ORDER BY rank
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@query", query);
            cmd.Parameters.AddWithValue("@limit", remainingLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var chunkId = reader.GetInt64(0);
                if (seenChunkIds.Add(chunkId))
                {
                    var chunkIndex = reader.GetInt32(1);
                    var offset = reader.GetInt32(2);
                    var length = reader.GetInt32(3);
                    var filePath = reader.GetString(4);
                    var rank = reader.GetDouble(5);

                    var contextText = SnippetFileHelper.ReadSnippetContext(filePath, offset, length);
                    var snippet = SnippetGenerator.CreateSnippet(contextText, rawQuery);

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        ChunkIndex = chunkIndex,
                        Snippet = snippet,
                        Score = -rank
                    });
                }
            }
        }
        catch { }
    }
}
