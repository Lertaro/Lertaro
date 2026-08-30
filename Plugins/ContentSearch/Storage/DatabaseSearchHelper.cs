using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text and short-term queries against FTS tables and extracts snippets directly from internal content.
/// </summary>
public static class DatabaseSearchHelper
{
    public static IReadOnlyList<SearchHitItem> Search(SqliteConnection conn, string rawQuery, string ftsQuery, int limit)
    {
        var hits = new List<SearchHitItem>();
        var seenFileIds = new HashSet<long>();
        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(ftsQuery) && tokens.Any(t => t.Length >= 3))
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

        if (hits.Count < limit && tokens.Length > 0 && tokens.All(t => t.Length < 3))
        {
            ScanContentForShortTokens(conn, tokens, rawQuery, limit, seenFileIds, hits);
        }

        return hits;
    }

    private static void ScanContentForShortTokens(
        SqliteConnection conn,
        string[] tokens,
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
            var whereClauses = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                whereClauses.Add($"files_fts.content LIKE @token{i}");
                cmd.Parameters.AddWithValue($"@token{i}", "%" + tokens[i] + "%");
            }

            cmd.CommandText = $"""
                SELECT f.id, f.path, files_fts.content
                FROM files_fts
                JOIN files f ON f.id = files_fts.rowid
                JOIN files ref ON ref.id = f.id OR ref.content_ref = f.id
                WHERE {string.Join(" AND ", whereClauses)}
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@limit", remainingLimit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (seenFileIds.Add(fileId))
                {
                    var filePath = reader.GetString(1);
                    var content = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    var snippet = SnippetGenerator.CreateSnippet(content, rawQuery);

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
            // Duplicate rows (content_ref set) own no FTS entry: a hit on the source row
            // must surface the duplicates too, and their snippet reuses the source text.
            cmd.CommandText = """
                WITH hits AS (
                    SELECT rowid AS src_id, rank, content FROM files_fts(@query) ORDER BY rank LIMIT @limit
                )
                SELECT f.id, f.path, hits.rank, COALESCE(src_fts.content, hits.content) AS content
                FROM hits
                JOIN files f ON f.id = hits.src_id OR f.content_ref = hits.src_id
                LEFT JOIN files_fts src_fts ON src_fts.rowid = f.content_ref
                ORDER BY hits.rank;
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
                    var content = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    var snippet = SnippetGenerator.CreateSnippet(content, rawQuery);

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
