using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text and multi-term fallback substring queries against database tables.
/// Split out purely to keep ContentSearchDatabase under the repository per-file line limit.
/// </summary>
public static class DatabaseSearchHelper
{
    public static IReadOnlyList<SearchHitItem> Search(SqliteConnection conn, string rawQuery, string ftsQuery, int limit)
    {
        var hits = new List<SearchHitItem>();
        var seenChunkIds = new HashSet<long>();

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT c.id, c.chunk_index, c.content, f.path, rank
                    FROM chunks_fts(@query)
                    JOIN chunks c ON c.id = chunks_fts.chunk_id
                    JOIN files f ON f.id = chunks_fts.file_id
                    ORDER BY rank
                    LIMIT @limit;
                    """;
                cmd.Parameters.AddWithValue("@query", ftsQuery);
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var chunkId = reader.GetInt64(0);
                    if (seenChunkIds.Add(chunkId))
                    {
                        var chunkIndex = reader.GetInt32(1);
                        var content = reader.GetString(2);
                        var filePath = reader.GetString(3);
                        var rank = reader.GetDouble(4);

                        hits.Add(new SearchHitItem
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                            ChunkIndex = chunkIndex,
                            Snippet = SnippetGenerator.CreateSnippet(content, rawQuery),
                            Score = -rank
                        });
                    }
                }
            }
            catch { }
        }

        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0 && hits.Count < limit)
        {
            var remainingLimit = limit - hits.Count;
            using var likeCmd = conn.CreateCommand();
            var whereClauses = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                whereClauses.Add($"(c.content LIKE @p{i} OR f.path LIKE @p{i})");
                likeCmd.Parameters.AddWithValue($"@p{i}", "%" + tokens[i].Trim() + "%");
            }

            likeCmd.CommandText = $"""
                SELECT c.id, c.chunk_index, c.content, f.path
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
                    var content = reader.GetString(2);
                    var filePath = reader.GetString(3);

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        ChunkIndex = chunkIndex,
                        Snippet = SnippetGenerator.CreateSnippet(content, rawQuery),
                        Score = 1.0
                    });
                }
            }
        }

        return hits;
    }
}
