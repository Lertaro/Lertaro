using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Executes full-text queries against the Lucene index and expands each hit to the source row
/// and every duplicate referencing it, mirroring the hit-expansion semantics the SQLite FTS5
/// backend used to provide. Split from the Lucene wrapper itself so the wrapper stays storage
/// mechanics only. Internal: the signature exposes the internal Lucene wrapper type; the
/// database facade is the only caller.
/// </summary>
internal static class DatabaseSearchHelper
{
    public static IReadOnlyList<SearchHitItem> Search(SqliteConnection conn, LuceneContentIndex lucene, string rawQuery, int limit)
    {
        var hits = new List<SearchHitItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // The top-N cap applies to indexed source rows, exactly like the FTS5 backend's
            // LIMIT-in-CTE: each hit then expands to the source itself and every duplicate
            // referencing it, so a duplicate cannot eat another file out of the limit.
            var luceneHits = lucene.Search(rawQuery, limit);
            foreach (var hit in luceneHits)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT id, path FROM files
                    WHERE path = @path OR content_ref = (SELECT id FROM files WHERE path = @path);
                    """;
                cmd.Parameters.AddWithValue("@path", hit.Path);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var filePath = reader.GetString(1);
                    if (!seenPaths.Add(filePath))
                        continue;

                    hits.Add(new SearchHitItem
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? string.Empty,
                        // A duplicate owns no text of its own: its snippet reuses the source
                        // row's content that Lucene stored with the hit.
                        Snippet = SnippetGenerator.CreateSnippet(hit.Content, rawQuery),
                        Score = hit.Score
                    });
                }
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Full-text search failed for '{rawQuery}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
        }

        return hits;
    }
}
