namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Combines lexical (BM25/FTS5) and semantic (vector) ranked search results using Reciprocal Rank Fusion (RRF).
/// </summary>
public static class HybridSearchMerger
{
    public const int DefaultK = 60;

    public static IReadOnlyList<SearchHitItem> MergeRrf(
        IReadOnlyList<SearchHitItem> lexicalHits,
        IReadOnlyList<SearchHitItem>? vectorHits = null,
        int k = DefaultK,
        int maxResults = 30)
    {
        if (lexicalHits.Count == 0 && (vectorHits == null || vectorHits.Count == 0))
            return Array.Empty<SearchHitItem>();

        if (k <= 0)
            k = DefaultK;

        var scoreMap = new Dictionary<string, (double TotalScore, SearchHitItem BestHit)>(StringComparer.OrdinalIgnoreCase);

        // Process lexical hits with 1-based ranks
        for (var i = 0; i < lexicalHits.Count; i++)
        {
            var hit = lexicalHits[i];
            var rank = i + 1;
            var rrfScore = 1.0 / (k + rank);

            if (scoreMap.TryGetValue(hit.FilePath, out var existing))
            {
                scoreMap[hit.FilePath] = (existing.TotalScore + rrfScore, existing.BestHit);
            }
            else
            {
                scoreMap[hit.FilePath] = (rrfScore, hit);
            }
        }

        // Process vector hits if present
        if (vectorHits != null)
        {
            for (var i = 0; i < vectorHits.Count; i++)
            {
                var hit = vectorHits[i];
                var rank = i + 1;
                var rrfScore = 1.0 / (k + rank);

                if (scoreMap.TryGetValue(hit.FilePath, out var existing))
                {
                    // Update score and prefer hit with richer snippet if needed
                    scoreMap[hit.FilePath] = (existing.TotalScore + rrfScore, existing.BestHit);
                }
                else
                {
                    scoreMap[hit.FilePath] = (rrfScore, hit);
                }
            }
        }

        var results = new List<SearchHitItem>(scoreMap.Count);
        foreach (var pair in scoreMap.Values)
        {
            results.Add(new SearchHitItem
            {
                FilePath = pair.BestHit.FilePath,
                FileName = pair.BestHit.FileName,
                DirectoryPath = pair.BestHit.DirectoryPath,
                ChunkIndex = pair.BestHit.ChunkIndex,
                Snippet = pair.BestHit.Snippet,
                Score = pair.TotalScore
            });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (results.Count > maxResults && maxResults > 0)
        {
            return results.GetRange(0, maxResults);
        }

        return results;
    }
}
