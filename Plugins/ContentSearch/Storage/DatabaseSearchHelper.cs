using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text and short-term queries against FTS tables and extracts snippets directly from internal content.
/// </summary>
public static class DatabaseSearchHelper
{
    /// <summary>
    /// Walks the index for the query and hands back each hit as the engine reaches it.
    /// </summary>
    /// <remarks>
    /// The short-token scan reads every indexed document, which on a real corpus takes seconds. Yielding as
    /// it goes is what lets a caller show something before the end: measured on a 669-document / 74 MB
    /// index, the first hit leaves the engine in under a millisecond while the whole walk still takes about
    /// 1.2 s. A collected list is therefore 1.2 s late and this is nearly immediate.
    ///
    /// Nothing is caught here. A failure reaches the caller, which is where the query, the provider and the
    /// user's expectation all live -- swallowing one to answer "no hits" is exactly what made a query that
    /// never ran indistinguishable from a search that found nothing.
    /// </remarks>
    public static IEnumerable<SearchHitItem> Search(SqliteConnection conn, string rawQuery, string ftsQuery, int limit)
    {
        if (limit <= 0 || string.IsNullOrWhiteSpace(rawQuery))
            yield break;

        var seenFileIds = new HashSet<long>();
        var tokens = rawQuery.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var emitted = 0;

        if (!string.IsNullOrWhiteSpace(ftsQuery) && tokens.Any(t => t.Length >= 3))
        {
            foreach (var hit in ExecuteFts(conn, ftsQuery, limit - emitted, seenFileIds))
            {
                emitted++;
                yield return hit;
            }

            // Nothing came of the terms ANDed above; concatenated they may still match text where the words
            // run together ("报告 单" against "报告单").
            if (emitted < limit && tokens.Length > 1)
            {
                var compacted = DatabaseFtsQueryHelper.BuildFtsQuery(string.Concat(tokens));
                if (!string.IsNullOrEmpty(compacted) && compacted != ftsQuery)
                {
                    foreach (var hit in ExecuteFts(conn, compacted, limit - emitted, seenFileIds))
                    {
                        emitted++;
                        yield return hit;
                    }
                }
            }

            yield break;
        }

        // FTS5's trigram index cannot answer a term shorter than three characters, so those fall to a scan
        // of the text itself. A query holding any long-enough term is answered by the index above and never
        // comes here.
        if (tokens.Length == 0 || tokens.Any(t => t.Length >= 3))
            yield break;

        foreach (var hit in ScanContentForShortTokens(conn, tokens, rawQuery, limit - emitted, seenFileIds))
            yield return hit;
    }

    // How many matched sources one duplicate-expansion query carries. Bounded only because a statement's
    // parameter count is -- the expansion itself is capped by nothing but the matches above it, exactly as
    // when this was one statement: LIMIT counts FTS matches, not the files they expand to.
    private const int DuplicateIdChunk = 500;

    private static IEnumerable<SearchHitItem> ScanContentForShortTokens(
        SqliteConnection conn,
        string[] tokens,
        string rawQuery,
        int limit,
        HashSet<long> seenFileIds)
    {
        // Snippet per matched source, reused by the expansion below. A duplicate owns no text of its own, so
        // its excerpt is generated once -- from the document its source matched in -- rather than once per
        // file that points at it.
        var snippetBySource = new Dictionary<long, string>();

        // One pass, no CTE. Reading this through `WITH matches AS (...)` twice -- how the source/duplicate
        // expansion used to work -- forces SQLite to materialise the entire match set, document text
        // included, into a temp B-tree before the first row can leave. On the same 669-document index that
        // turned 1.2 s into 9.6 s and pushed the first hit out to 1.6 s.
        //
        // This reader closes before the expansion runs: no second statement can be prepared against this
        // connection while the first is still reading.
        using (var cmd = conn.CreateCommand())
        {
            for (var i = 0; i < tokens.Length; i++)
                cmd.Parameters.AddWithValue($"@token{i}", "%" + tokens[i] + "%");
            cmd.Parameters.AddWithValue("@limit", limit);
            // A window, not the document. The WHERE already reads every indexed document, but returning
            // `content` handed the caller the WHOLE of every match on top of that: measured on a 496-document
            // index asking for 2000 rows, 35 MB of text materialised into .NET strings per query -- and the
            // snippet only ever shows ~120 characters of it. Centering a 1000-character window on the first
            // token cuts that to 26 KB while leaving SnippetGenerator the same reach it had before (its own
            // fuzzy probe is bounded at 1000 characters).
            //
            // instr() is case-sensitive where LIKE is not, so a term that matched only case-insensitively
            // centers at 0 and the window falls back to the document's opening -- which is exactly what
            // SnippetGenerator already does when it finds no match. ponytail: the tail "..." that means
            // "the document continues" is now decided against the window, so it can be missing on a
            // truncated hit. Upgrade path: return length(content) too and decide the ellipsis from that.
            cmd.Parameters.AddWithValue("@window", tokens[0]);
            cmd.CommandText = $"""
                SELECT f.id, f.path, files_fts.rowid,
                       substr(files_fts.content, max(1, instr(files_fts.content, @window) - 300), 1000)
                FROM files_fts
                JOIN files f ON f.id = files_fts.rowid
                WHERE {string.Join(" AND ", tokens.Select((_, i) => $"files_fts.content LIKE @token{i}"))}
                LIMIT @limit;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (!seenFileIds.Add(fileId)) continue;

                var sourceId = reader.GetInt64(2);
                var content = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var snippet = SnippetGenerator.CreateSnippet(content, rawQuery);
                snippetBySource[sourceId] = snippet;
                yield return CreateHit(reader.GetString(1), snippet, 1.0);
            }
        }

        foreach (var hit in ExpandDuplicates(conn, seenFileIds, snippetBySource))
            yield return hit;
    }

    // Duplicates own no FTS entry, so a matched source expands to every file referencing it. Seeking those
    // off idx_files_content_ref is the cheap half: no document text is read again, and the excerpt already
    // generated for the source is reused verbatim.
    private static IEnumerable<SearchHitItem> ExpandDuplicates(
        SqliteConnection conn,
        HashSet<long> seenFileIds,
        Dictionary<long, string> snippetBySource)
    {
        var ids = snippetBySource.Keys.ToArray();
        for (var from = 0; from < ids.Length; from += DuplicateIdChunk)
        {
            var count = Math.Min(DuplicateIdChunk, ids.Length - from);
            using var cmd = conn.CreateCommand();
            // Bound as parameters rather than inlined: these are values that came out of a query, and
            // pasting values into the text of a statement is how an injection gets a foothold.
            for (var i = 0; i < count; i++)
                cmd.Parameters.AddWithValue($"@src{i}", ids[from + i]);
            cmd.CommandText = $"SELECT id, path, content_ref FROM files WHERE content_ref IN ({string.Join(",", Enumerable.Range(0, count).Select(i => $"@src{i}"))});";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var fileId = reader.GetInt64(0);
                if (!seenFileIds.Add(fileId)) continue;
                yield return CreateHit(reader.GetString(1), snippetBySource[reader.GetInt64(2)], 1.0);
            }
        }
    }

    private static SearchHitItem CreateHit(string filePath, string snippet, double score) => new()
    {
        FilePath = filePath,
        FileName = Path.GetFileName(filePath),
        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
        Snippet = snippet,
        Score = score
    };

    private static IEnumerable<SearchHitItem> ExecuteFts(SqliteConnection conn, string query, int limit, HashSet<long> seenFileIds)
    {
        if (limit <= 0)
            yield break;

        using var cmd = conn.CreateCommand();
        // Duplicate rows (content_ref set) own no FTS entry: a hit on the source row surfaces the duplicates
        // too, reusing the snippet FTS5 generated for the source.
        //
        // The two memberships are two SELECTs, not one OR. `f.id = src OR f.content_ref = src` can use no
        // index on either side, so SQLite rescanned `files` once per hit -- quadratic in the limit the full
        // window passes (2000). Each half now seeks: `id` is the rowid alias, `content_ref` has
        // idx_files_content_ref.
        //
        // This CTE is left to materialise on purpose, unlike the short-token scan: it carries a rank and a
        // 32-token excerpt per hit rather than the document, so the temp copy stays small, and bm25 has to
        // see the whole hit set before it can promise the hits leave in rank order.
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
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var fileId = reader.GetInt64(0);
            if (!seenFileIds.Add(fileId)) continue;

            var filePath = reader.GetString(1);
            var rank = reader.GetDouble(2);
            var snip = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            // FTS5's snippet is already the right window around the match; only the whitespace needs
            // flattening for a single-line row. Building one from the whole document instead would mean
            // reading the whole document out here.
            yield return CreateHit(filePath, SnippetGenerator.NormalizeWhitespace(snip), -rank);
        }
    }
}
