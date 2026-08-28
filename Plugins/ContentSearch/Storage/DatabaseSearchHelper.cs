using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text and fallback queries against files tables and resolves snippets on-demand from files.
/// Split out purely to keep ContentSearchDatabase under the repository per-file line limit.
/// </summary>
public static class DatabaseSearchHelper
{
    public static IReadOnlyList<SearchHitItem> Search(SqliteConnection conn, string rawQuery, string ftsQuery, int limit)
    {
        var hits = new List<SearchHitItem>();
        var seenFileIds = new HashSet<long>();
        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            ExecuteFts(conn, ftsQuery, rawQuery, limit, seenFileIds, hits);

            if (hits.Count < limit && tokens.Length > 1)
            {
                var compacted = DatabaseFtsQueryHelper.BuildFtsQuery(string.Concat(tokens));
                if (!string.IsNullOrEmpty(compacted) && compacted != ftsQuery)
                {
                    ExecuteFts(conn, compacted, rawQuery, limit, seenFileIds, hits);
                }
            }
        }

        // Handle short (< 3 chars) queries where Contentless trigram FTS cannot verify match in isolation
        if (hits.Count < limit && tokens.Length > 0 && tokens.All(t => t.Length < 3))
        {
            ScanFilesForShortQuery(conn, tokens, rawQuery, limit, seenFileIds, hits);
        }

        if (tokens.Length > 0 && hits.Count < limit)
        {
            var remainingLimit = limit - hits.Count;
            using var likeCmd = conn.CreateCommand();
            var whereClauses = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                whereClauses.Add($"path LIKE @p{i}");
                likeCmd.Parameters.AddWithValue($"@p{i}", "%" + tokens[i].Trim() + "%");
            }

            likeCmd.CommandText = $"""
                SELECT id, path
                FROM files
                WHERE {string.Join(" AND ", whereClauses)}
                LIMIT @limit;
                """;
            likeCmd.Parameters.AddWithValue("@limit", remainingLimit);

            using var reader = likeCmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (seenFileIds.Add(fileId))
                {
                    var filePath = reader.GetString(1);
                    var snippet = SnippetFileHelper.CreateFileSnippet(filePath, rawQuery);

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        Snippet = snippet,
                        Score = 1.0
                    });
                }
            }
        }

        return hits;
    }

    private static void ScanFilesForShortQuery(
        SqliteConnection conn,
        string[] tokens,
        string rawQuery,
        int limit,
        HashSet<long> seenFileIds,
        List<SearchHitItem> hits)
    {
        try
        {
            using var fileScanCmd = conn.CreateCommand();
            fileScanCmd.CommandText = "SELECT id, path FROM files LIMIT 200;";
            using var reader = fileScanCmd.ExecuteReader();
            while (reader.Read() && hits.Count < limit)
            {
                var fileId = reader.GetInt64(0);
                if (seenFileIds.Add(fileId))
                {
                    var filePath = reader.GetString(1);
                    var snippet = SnippetFileHelper.CreateFileSnippet(filePath, rawQuery);
                    if (!string.IsNullOrEmpty(snippet) && tokens.All(t => snippet.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(new SearchHitItem
                        {
                            FilePath = filePath,
                            FileName = Path.GetFileName(filePath),
                            DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
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
        HashSet<long> seenFileIds,
        List<SearchHitItem> hits)
    {
        try
        {
            var remainingLimit = limit - hits.Count;
            if (remainingLimit <= 0) return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT f.id, f.path, rank
                FROM files_fts(@query)
                JOIN files f ON f.id = files_fts.rowid
                ORDER BY rank
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@query", query);
            cmd.Parameters.AddWithValue("@limit", remainingLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (seenFileIds.Add(fileId))
                {
                    var filePath = reader.GetString(1);
                    var rank = reader.GetDouble(2);
                    var snippet = SnippetFileHelper.CreateFileSnippet(filePath, rawQuery);

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        Snippet = snippet,
                        Score = -rank
                    });
                }
            }
        }
        catch { }
    }
}
