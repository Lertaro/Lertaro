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
            // Fork: favorites and history ride the index-rule priority system instead of a separate key order.
            // They are the rows the owner asked to see first, so they take the High band and everything upstream
            // already does with High (type order, match quality, the engine cut) applies inside that band.
            // HistoryCount > 0, not HistoryPriority: the Windows "Recent Items" rows carry a non-MaxValue
            // HistoryPriority but a zero count, and they must not be lifted to the top by this rule.
            if (candidate.IsFavorite || EffectiveHistoryCount(candidate) > 0)
                return IndexRuleEffect.High;
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
                     // Fork grouping (the owner's rule): three bands, in this order --
                     //   0. favorites and rows the user actually searched for before,
                     //   1. Windows "Recent Items" rows (recently opened, never searched from here),
                     //   2. ordinary results (apps, files, settings -- ordered by the result-type order).
                     // The band leads: a favorite or a row the user already searched for must be visible on
                     // the first keystroke, before any ordinary result, and a recently-opened row likewise
                     // comes before ordinary results. The left edge then orders the rows INSIDE a band.
                     .OrderBy(c => Band(c) == 0 ? 0 : 1)
                     // The user's result-type order decides before any match key: with Applications above
                     // Files in the settings, every application comes before every file, however well a
                     // file happens to match. (Before this, a file literally named "db" outranked an
                     // application that reached the query only through its pinyin initials, so the setting
                     // looked broken.)
                     .ThenBy(c => c.TypeRank)
                     // Inside one type: the left edge, then coverage, then how often and how recently the
                     // row was opened, then the index-rule band. Recent files carry the Files type rank, so
                     // they lead the Files group and never appear above an earlier type.
                     .ThenBy(c => EffectiveMatch(c).Start)
                     .ThenByDescending(c => EffectiveMatch(c).Coverage)
                     .ThenByDescending(c => EffectiveHistoryCount(c))
                     .ThenBy(c => EffectiveHistoryCount(c) > 0 ? c.HistoryPriority : int.MaxValue)
                     .ThenBy(c => SearchResultRankComparer.PriorityRank(Priority(c)))
                     .ThenBy(c => Band(c) == 1 ? 0 : 1)
                     .ThenBy(c => EffectiveMatch(c), MatchRankQualityComparer.Instance)
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

    // How often the user actually searched for / opened this path from this app. A Windows "Recent Items"
    // row used to be counted as 1 here (it carries a priority but no count), which gave it the same weight as
    // a row searched once and floated it above ordinary results; band 1 keeps it in its own group instead.
    private static int EffectiveHistoryCount(SearchResultMapper.RankedCandidate candidate)
        => candidate.HistoryCount;

    // The owner's bands, in rank order:
    //   0 -- a favorite, or a row the user actually searched for from this app before;
    //   1 -- a Windows "Recent Items" row: recently opened in the OS, never searched here;
    //   2 -- an ordinary result (app, file, setting, ...).
    // HistoryPriority alone cannot tell 1 from 2 (a Recent Items row is built with a small priority but a
    // zero count), so a zero count paired with a real priority is what marks band 1.
    private static int Band(SearchResultMapper.RankedCandidate candidate)
    {
        if (candidate.IsFavorite || candidate.HistoryCount > 0)
            return 0;
        return candidate.HistoryPriority != int.MaxValue ? 1 : 2;
    }
}
