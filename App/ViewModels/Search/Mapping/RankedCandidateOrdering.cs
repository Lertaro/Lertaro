using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// The one ordering ladder every result list shares: candidates in, best-first rows out.
//
// Split out of SearchResultMapper purely to keep that file under the repo's per-file line limit. It has no
// state of its own -- it always operates on the candidate list its caller built.
internal static class RankedCandidateOrdering
{
    // User signals first, then match quality, then a stable tie-break:
    //
    //   * IsCurated / Priority -- favorites and history ("you use this"), which outrank any textual match.
    //   * TypeRank              -- the user's own result-type order (quick window only).
    //   * Match.Start           -- "left-side match priority": the query hitting furthest left in the name
    //                              leads the match keys. It used to sit behind Shape/RecencyBand/Depth, and
    //                              on a broad query the display cap then filled with rows that merely
    //                              matched a whole word or were touched today, cutting off the row the user
    //                              was looking for (reported as a plain drop in hit rate).
    //   * Match.Weight          -- then coverage*contiguity.
    //   * Match.Tier            -- then the WEAKEST match key: a literal name hit beats a shorthand alias
    //                              hit beats a full transliteration (英文 > 简拼 > 全拼). Placed last so it
    //                              only separates rows that already agree on position and coverage;
    //                              see AliasMatchRules.TierFor.
    //   * files before folders  -- only when the match is otherwise equal.
    //   * path length, path     -- then the stable, deterministic tail.
    //
    // Match.Shape, RecencyBand and Depth are still computed and carried on the candidate (RankSignals for
    // index rows) but deliberately order nothing since that report -- a consumer that wants them has them,
    // and none of them may push a left-hand hit off the screen.
    //
    // SearchResultRelevance packs the same match keys into RankSortKey for the full window and the engine.
    //
    // The inline window layers directory proximity ABOVE all of this separately (see
    // ExplorerSearchHelper.CreateLocalSnapshot), so a direct child wins even against a curated deeper row.
    public static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates)
    {
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SearchResultMapper.RankedCandidate>();
        foreach (var candidate in candidates
                     .OrderByDescending(c => c.IsCurated)
                     .ThenBy(c => c.Priority)
                     .ThenBy(c => c.TypeRank)
                     .ThenBy(c => c.Match.Start)
                     .ThenByDescending(c => c.Match.Weight)
                     .ThenBy(c => c.Match.Tier)
                     .ThenBy(c => c.Result.IsDir)
                     .ThenBy(c => c.NormalizedPath.Length)
                     .ThenBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate);
        }
        return ranked;
    }

    // The recency band and path depth of one index row, as the engine's sort key reads them.
    internal static (int Band, int Depth) SignalsOf(FileMetadata metadata, string path, DateTime nowUtc) =>
        (RankSignals.RecencyBand(metadata.Created, metadata.Modified, nowUtc), RankSignals.PathDepth(path));
}
