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

            // Same direction as ExecuteFts: only source rows own an FTS entry, so a match
            // must expand to the source itself AND every duplicate referencing it, with
            // the snippet reusing the source text. LIMIT caps the FTS matches, not the
            // expanded rows, so duplicates cannot eat other files out of the limit.
            //
            // The source's own text is the second SELECT's value, so a duplicate needs no join
            // back into files_fts -- it already travels with the match it expands from.
            //
            // ponytail: the matched document text still travels once per expanded row, not once
            // per match. Bounding that needs the expansion split into two round trips (ids first,
            // then text for the distinct ids only), which is worth it once duplicate-heavy corpora
            // make the expansion wider than the matches. The snippet itself is deduped below, so
            // the expensive part (CreateSnippet's scan of the text) is already paid once per match.
            cmd.CommandText = $"""
                WITH matches AS (
                    SELECT rowid AS src_id, content
                    FROM files_fts
                    WHERE {string.Join(" AND ", whereClauses)}
                    LIMIT @limit
                )
                SELECT f.id, f.path, matches.src_id, matches.content
                FROM matches JOIN files f ON f.id = matches.src_id
                UNION ALL
                SELECT f.id, f.path, matches.src_id, matches.content
                FROM matches JOIN files f ON f.content_ref = matches.src_id;
                """;
            cmd.Parameters.AddWithValue("@limit", remainingLimit);

            // Snippets are keyed by the source the text came from, so a source with many duplicates
            // has its text scanned once rather than once per duplicate.
            var snippetBySourceId = new Dictionary<long, string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (seenFileIds.Add(fileId))
                {
                    var filePath = reader.GetString(1);
                    var sourceId = reader.GetInt64(2);
                    if (!snippetBySourceId.TryGetValue(sourceId, out var snippet))
                    {
                        var content = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        snippet = SnippetGenerator.CreateSnippet(content, rawQuery);
                        snippetBySourceId[sourceId] = snippet;
                    }

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
        catch (Exception ex)
        {
            // Logged instead of swallowed: an empty catch here answers "nothing matches" for a query that
            // never ran, and the user has no way to tell that apart from a genuine no-results search.
            PluginSdk.Logger.Log($"[ContentSearch] Short-token content scan failed: {ex.Message}", PluginSdk.LogLevel.Error);
        }
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
            // surfaces the duplicates too, reusing the source snippet generated by FTS5.
            //
            // The two memberships are two SELECTs, not one OR. `f.id = src OR f.content_ref = src`
            // cannot use an index on either side, so SQLite had to rescan `files` once per hit --
            // quadratic in the limit the full window passes (2000), which is what froze the UI
            // thread on a large content index. Each half now seeks: `id` is the rowid alias,
            // `content_ref` has idx_files_content_ref.
            cmd.CommandText = """
                WITH hits AS (
                    SELECT rowid AS src_id, rank, snippet(files_fts, 0, '', '', '...', 32) AS snip
                    FROM files_fts(@query)
                    ORDER BY rank
                    LIMIT @limit
                )
                SELECT f.id, f.path, hits.rank, hits.snip
                FROM hits JOIN files f ON f.id = hits.src_id
                UNION ALL
                SELECT f.id, f.path, hits.rank, hits.snip
                FROM hits JOIN files f ON f.content_ref = hits.src_id
                ORDER BY 3;
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
                    var snip = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    var snippet = SnippetGenerator.NormalizeWhitespace(snip);

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
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[ContentSearch] FTS query failed: {ex.Message}", PluginSdk.LogLevel.Error);
        }
    }
}
