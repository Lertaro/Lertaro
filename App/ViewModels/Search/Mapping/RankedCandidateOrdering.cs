using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search.Mapping;

// The one ordering ladder every result list shares: candidates in, best-first rows out.
//
// Keys, strongest first:
//   1. Folder priority -- low (build output, Windows.old) sorts below normal, high above it. It is the
//      hard grouping: a high rule beats a normal row of ANY result type, and a low rule loses to every
//      normal one -- otherwise a high-priority file would still sit under a pile of normal applications.
//   2. Result-type order -- the user's own order (quick window only; files are one type elsewhere). It
//      groups the rows inside one priority band.
//   3. Match quality -- contiguity, shape (whole word > word start > inside > scattered), left-contiguous
//      (shape tie-break only), start, coverage, tier (MatchRank.CompareQuality).
//   4. Favorites -- an explicit user pick, after quality but before history.
//   5. History band -- 5+ opens (0), 1-4 opens (1), none (2). History never overrules a better match.
//   6. History priority -- the learned-keyword order inside a band.
//   7. Files before folders.
//   8. Natural name order, then name length, path length, drive and path.
//
// The ladder used to compare type order before priority and all six quality keys first, which made
// either setting unreachable: two rows of different types practically never tie on the quality keys, and
// a high-priority file could never climb above an application group. Priority is the outer grouping now;
// type and quality only decide the order inside one band.
//
// Shape is part of MatchRank.CompareQuality; recency band and path depth are deliberately absent.
internal static class RankedCandidateOrdering
{
    private const string ApplicationKind = "Application";

    public static List<SearchResultMapper.RankedCandidate> Order(
        List<SearchResultMapper.RankedCandidate> candidates,
        string? query = null,
        Func<string, bool, IndexRuleEffect>? priorityResolver = null)
    {
        _ = query;
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<SearchResultMapper.RankedCandidate>();
        // Per call, so a search decides each directory once; the resolver itself caches ancestors.
        var priorities = new Dictionary<string, IndexRuleEffect>(StringComparer.OrdinalIgnoreCase);
        IndexRuleEffect Priority(SearchResultMapper.RankedCandidate candidate)
        {
            if (priorityResolver == null)
                return IndexRuleEffect.Normal;
            var path = candidate.NormalizedPath.TrimEnd('\\');
            if (priorities.TryGetValue(path, out var known))
                return known;
            var priority = priorityResolver(path, candidate.Result.IsDir);
            priorities[path] = priority;
            return priority;
        }

        foreach (var candidate in ApplicationOwned(candidates)
                     .OrderBy(c => SearchResultRankComparer.PriorityRank(Priority(c)))
                     .ThenBy(c => c.TypeRank)
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

    // One path, two views: an indexed shortcut row ("File") and a provider's application row describe
    // the same on-disk .lnk. They tie on every text key, so the type order is what decides which one
    // survives the path dedupe below -- and with Files configured ahead of applications that was the
    // plain-file view: every Start Menu / Desktop app came back named "....lnk" with a file icon, and the
    // application row (extension-free title, app icon, launch delegate) was dropped as a duplicate. Only
    // the application row can actually launch the app, so it owns its path; the file row is just a second
    // rendering of the same shortcut and yields before the sort rather than competing with it.
    private static IEnumerable<SearchResultMapper.RankedCandidate> ApplicationOwned(List<SearchResultMapper.RankedCandidate> candidates)
    {
        var applicationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Result.ResultKind, ApplicationKind, StringComparison.Ordinal))
                applicationPaths.Add(candidate.NormalizedPath);
        }

        return applicationPaths.Count == 0
            ? candidates
            : candidates.Where(c => string.Equals(c.Result.ResultKind, ApplicationKind, StringComparison.Ordinal)
                                    || !applicationPaths.Contains(c.NormalizedPath));
    }

    // Older callers can supply the priority map without the count map; a path present there is weak
    // (1-4) history, matching SearchResultRankComparer's own fallback.
    private static int EffectiveHistoryCount(SearchResultMapper.RankedCandidate candidate)
        => candidate.HistoryCount > 0 ? candidate.HistoryCount
            : candidate.HistoryPriority != int.MaxValue ? 1 : 0;
}
