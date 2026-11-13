using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Weak recall for confirmed query associations whose stored spelling is related to what the user is
// typing now. These rows are deliberately kept in HistoryTail and capped at two: they recover a target the
// bounded global page may have dropped, without giving a partial-keyword guess the power to reorder direct
// matches. The catalog is rebuilt only when associations change.
internal static class RelatedHistoryCandidateMapper
{
    internal const int MaxCandidates = 2;

    internal readonly record struct CatalogEntry(HistoryEntry Entry, string Keyword);

    internal sealed record CatalogSnapshot(long Version, IReadOnlyList<CatalogEntry> Entries);

    private static readonly object Gate = new();
    private static CatalogSnapshot _catalog = new(-1, []);

    internal static Func<long> VersionSource { get; set; } = () => SearchHistoryStore.AssociationVersion;
    internal static Func<IEnumerable<HistoryEntry>> EntriesSource { get; set; } =
        SearchHistoryStore.GetLearnedAssociationEntries;

    public static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        RankContext? rankContext,
        int typeRank,
        Func<string, bool>? pathFilter = null,
        Action<string, RankingCandidateSource>? onScopeDropped = null) =>
        CollectCurrentCatalog(
            fuzzy, scope,
            HistorySearchCandidateMapper.SafeFileExists,
            HistorySearchCandidateMapper.SafeDirectoryExists,
            rankContext, typeRank, pathFilter, onScopeDropped);

    internal static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        IEnumerable<HistoryEntry> entries,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext = null,
        int typeRank = int.MaxValue,
        Func<string, bool>? pathFilter = null,
        Action<string, RankingCandidateSource>? onScopeDropped = null)
    {
        var matches = MatchRelated(fuzzy.Text, entries, int.MaxValue);
        return CollectCatalog(
            fuzzy, scope, matches, fileExists, directoryExists, rankContext, typeRank,
            pathFilter, onScopeDropped);
    }

    private static List<SearchResultMapper.RankedCandidate> CollectCurrentCatalog(
        FuzzyQuery fuzzy,
        string? scope,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext,
        int typeRank,
        Func<string, bool>? pathFilter,
        Action<string, RankingCandidateSource>? onScopeDropped)
    {
        CatalogSnapshot snapshot;
        lock (Gate)
        {
            var version = VersionSource();
            if (_catalog.Version != version)
                _catalog = BuildCatalog(version, EntriesSource());
            snapshot = _catalog;
        }

        // Match, scope and validity share one pass, so the two-candidate cap only sees usable rows.
        // ponytail: this scans the learned-association catalog linearly per query; if it ever grows large,
        // add an inverted relation/query index rather than restoring a pre-scope fixed prefix.
        return CollectCatalog(
            fuzzy, scope, snapshot.Entries, fileExists, directoryExists,
            rankContext, typeRank, pathFilter, onScopeDropped);
    }

    private static List<SearchResultMapper.RankedCandidate> CollectCatalog(
        FuzzyQuery fuzzy,
        string? scope,
        IReadOnlyList<CatalogEntry> matches,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext,
        int typeRank,
        Func<string, bool>? pathFilter,
        Action<string, RankingCandidateSource>? onScopeDropped)
    {
        if (matches.Count == 0)
            return [];

        var normalizedScope = string.IsNullOrEmpty(scope) ? null : SearchResultHelper.NormalizePath(scope);
        var candidates = new List<ScoredCandidate>(MaxCandidates);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            var relation = RelationOf(match.Keyword, fuzzy.Text);
            if (relation.Kind is RelationKind.Exact or RelationKind.None)
                continue;
            if (!HistorySearchCandidateMapper.TryCreateResult(
                    match.Entry, fuzzy.Text, normalizedScope, fileExists, directoryExists,
                    out var result, out var normalizedPath))
                continue;
            if (pathFilter != null && !pathFilter(normalizedPath))
            {
                onScopeDropped?.Invoke(normalizedPath, RankingCandidateSource.RelatedHistory);
                continue;
            }
            if (!paths.Add(normalizedPath))
                continue;
            if (rankContext?.PriorityResolver?.Invoke(normalizedPath, result.IsDir).Effect == IndexRuleEffect.Low)
                continue;
            if (HistorySearchCandidateMapper.IsExcludedFromSearch(result))
                continue;

            // A related recall is a distinct source, not an unconfirmed learned alias. Opening it teaches
            // the current query; IsLearnedAliasRecall remains reserved for exact learned lookups.
            var byName = fuzzy.Rank(result.Name);
            var candidate = new SearchResultMapper.RankedCandidate(
                result,
                IsFavorite: false,
                HistoryCount: match.Entry.Count,
                TypeRank: typeRank,
                Match: byName.IsMatch
                    ? byName
                    : new MatchRank(
                        MatchRank.TierName,
                        relation.Start,
                        Math.Min(1d, (double)fuzzy.Text.Length / Math.Max(1, match.Keyword.Length)),
                        1d,
                        MatchRank.ShapeWholeWord),
                NormalizedPath: normalizedPath,
                // A related-keyword recall is a weak association, never a direct query hit. It moves
                // into HistoryTail even when the target name happens to match; a real index hit for the
                // same path merges with it and restores direct-match ranking in RankedCandidateOrdering.
                IsHistoryOnly: true,
                Source: RankingCandidateSource.History | RankingCandidateSource.RelatedHistory);
            KeepBest(candidates, new ScoredCandidate(candidate, match, relation.Kind));
        }
        return candidates.Select(candidate => candidate.Candidate).ToList();
    }

    internal static CatalogSnapshot BuildCatalog(long version, IEnumerable<HistoryEntry> entries)
    {
        var catalog = new List<CatalogEntry>();
        foreach (var entry in entries)
        {
            var keyword = SearchHistoryStore.NormalizeAssociationQuery(entry.Keyword);
            if (keyword.Length == 0)
                continue;
            catalog.Add(new CatalogEntry(entry, keyword));
        }
        return new CatalogSnapshot(version, catalog);
    }

    internal static IReadOnlyList<CatalogEntry> MatchRelated(
        string query,
        IEnumerable<HistoryEntry> entries,
        int maxMatches) =>
        MatchCatalog(query, BuildCatalog(version: 0, entries).Entries, maxMatches);

    internal static IReadOnlyList<CatalogEntry> MatchCatalog(
        string query,
        IReadOnlyList<CatalogEntry> catalog,
        int maxMatches)
    {
        if (!IsPlainQuery(query) || maxMatches <= 0)
            return [];

        var matches = new List<ScoredCatalogEntry>(Math.Min(maxMatches, catalog.Count));
        foreach (var candidate in catalog)
        {
            var relation = RelationOf(candidate.Keyword, query);
            if (relation.Kind == RelationKind.Exact)
                continue;
            if (relation.Kind == RelationKind.None)
                continue;
            KeepBestCatalog(matches, new ScoredCatalogEntry(candidate, relation.Kind), maxMatches);
        }
        return matches.Select(match => match.Entry).ToList();
    }

    private static (RelationKind Kind, int Start) RelationOf(string keyword, string query)
    {
        if (keyword.Equals(query, StringComparison.OrdinalIgnoreCase))
            return (RelationKind.Exact, 0);

        var prefix = keyword.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (prefix == 0)
            return (RelationKind.Prefix, 0);
        if (prefix > 0)
            return (RelationKind.Contains, prefix);
        if (query.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return (RelationKind.QueryExtends, 0);
        return (RelationKind.None, 0);
    }

    private static bool IsPlainQuery(string query)
    {
        if (query.Length is < 2 or > 64)
            return false;
        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }

    private readonly record struct ScoredCandidate(
        SearchResultMapper.RankedCandidate Candidate,
        CatalogEntry Entry,
        RelationKind Relation);

    private readonly record struct ScoredCatalogEntry(CatalogEntry Entry, RelationKind Relation);

    private static void KeepBest(List<ScoredCandidate> best, ScoredCandidate candidate)
    {
        var index = 0;
        while (index < best.Count
               && Compare(candidate.Entry, candidate.Relation, best[index].Entry, best[index].Relation) >= 0)
            index++;
        if (index >= MaxCandidates)
            return;
        best.Insert(index, candidate);
        if (best.Count > MaxCandidates)
            best.RemoveAt(MaxCandidates);
    }

    private static void KeepBestCatalog(
        List<ScoredCatalogEntry> best,
        ScoredCatalogEntry candidate,
        int maxMatches)
    {
        var index = 0;
        while (index < best.Count
               && Compare(candidate.Entry, candidate.Relation, best[index].Entry, best[index].Relation) >= 0)
            index++;
        if (index >= maxMatches)
            return;
        best.Insert(index, candidate);
        if (best.Count > maxMatches)
            best.RemoveAt(maxMatches);
    }

    private static int Compare(
        CatalogEntry left,
        RelationKind leftRelation,
        CatalogEntry right,
        RelationKind rightRelation)
    {
        var compare = leftRelation.CompareTo(rightRelation);
        if (compare != 0)
            return compare;
        compare = right.Entry.Time.CompareTo(left.Entry.Time);
        if (compare != 0)
            return compare;
        compare = right.Entry.Count.CompareTo(left.Entry.Count);
        if (compare != 0)
            return compare;
        compare = string.Compare(left.Keyword, right.Keyword, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;
        return string.Compare(left.Entry.Path, right.Entry.Path, StringComparison.OrdinalIgnoreCase);
    }

    private enum RelationKind
    {
        Prefix = 0,
        Contains = 1,
        QueryExtends = 2,
        Exact = 3,
        None = 4,
    }
}
