using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search.Mapping;

// The one ordering ladder every result list shares: candidates in, best-first rows out.
//
// Keys, strongest first:
//   1. Result-type order -- the user's own order (quick window only; files are one type elsewhere). It is a
//      hard grouping: a row of an earlier type beats a row of a later one whatever the textual match was,
//      and every key below only decides the order INSIDE one type.
//   2. Match quality -- contiguity, shape (whole word > word start > inside > scattered), left-contiguous
//      (shape tie-break only), start, coverage, tier (MatchRank.CompareQuality).
//   3. Favorites -- an explicit user pick, after quality but before history.
//   4. History band -- 5+ opens (0), 1-4 opens (1), none (2). History never overrules a better match.
//   5. History priority -- the learned-keyword order inside a band.
//   6. Files before folders.
//   7. Natural name order, then name length, path length, drive and path.
//
// The ladder used to compare type order after all six quality keys, which made the setting unreachable:
// two rows of different types practically never tie on contiguity+shape+left+start+coverage+tier, so the
// configured order could not move anything. Quality is the engine's own ranking and stays the inner order.
//
// Shape is part of MatchRank.CompareQuality; recency band and path depth are deliberately absent.
internal static class RankedCandidateOrdering
{
    public static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates, string? query = null)
    {
        _ = query;
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SearchResultMapper.RankedCandidate>();
        foreach (var candidate in candidates
                     .OrderBy(c => c.TypeRank)
                     .ThenBy(c => EffectiveMatch(c), MatchRankQualityComparer.Instance)
                     .ThenBy(c => c.IsFavorite ? 0 : 1)
                     .ThenBy(c => SearchResultRankComparer.HistoryBand(EffectiveHistoryCount(c)))
                     .ThenBy(c => c.HistoryPriority)
                     .ThenBy(c => c.Result.IsDir)
                     .ThenBy(c => c.Result.Name, SearchNameOrderComparer.Instance)
                     .ThenBy(c => c.Result.Name.Length)
                     .ThenBy(c => c.NormalizedPath.Length)
                     .ThenBy(c => c.Result.Drive, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate);
        }
        return ranked;
    }

    private static MatchRank EffectiveMatch(SearchResultMapper.RankedCandidate candidate)
        => candidate.IsHistoryOnly ? MatchRank.NoMatch : candidate.Match;

    // Older callers can supply the priority map without the count map; a path present there is weak
    // (1-4) history, matching SearchResultRankComparer's own fallback.
    private static int EffectiveHistoryCount(SearchResultMapper.RankedCandidate candidate)
        => candidate.HistoryCount > 0 ? candidate.HistoryCount
            : candidate.HistoryPriority != int.MaxValue ? 1 : 0;
}
