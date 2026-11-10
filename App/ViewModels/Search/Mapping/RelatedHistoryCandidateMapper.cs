using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Weak recall for confirmed query associations whose stored spelling is related to what the user is
// typing now. These rows are deliberately kept in HistoryTail and capped at two: they recover a target the
// bounded global page may have dropped, without giving a partial-keyword guess the power to reorder direct
// matches. The catalog carries precomputed character masks and is rebuilt only when associations change.
internal static class RelatedHistoryCandidateMapper
{
    internal const int MaxCandidates = 2;
    private const int MaxCachedMatches = 16;
    private const int CacheCapacity = 128;
    private const long CacheLifetimeMs = 30_000;

    private sealed class CacheEntry
    {
        public required IReadOnlyList<CatalogEntry> Matches { get; init; }
        public required long LastUsed { get; init; }
    }

    internal readonly record struct CatalogEntry(HistoryEntry Entry, string Keyword, UInt128 Mask);

    private sealed record CatalogSnapshot(long Version, IReadOnlyList<CatalogEntry> Entries);

    private static readonly object Gate = new();
    private static CatalogSnapshot _catalog = new(-1, []);
    private static readonly Dictionary<string, CacheEntry> QueryCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        RankContext? rankContext,
        int typeRank,
        Func<string, bool>? pathFilter = null) =>
        CollectCatalog(
            fuzzy, scope, CurrentMatches(fuzzy.Text),
            HistorySearchCandidateMapper.SafeFileExists,
            HistorySearchCandidateMapper.SafeDirectoryExists,
            rankContext, typeRank, pathFilter);

    internal static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        IEnumerable<HistoryEntry> entries,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext = null,
        int typeRank = int.MaxValue,
        Func<string, bool>? pathFilter = null)
    {
        var matches = MatchRelated(fuzzy.Text, entries, MaxCachedMatches);
        return CollectCatalog(
            fuzzy, scope, matches, fileExists, directoryExists, rankContext, typeRank, pathFilter);
    }

    private static List<SearchResultMapper.RankedCandidate> CollectCatalog(
        FuzzyQuery fuzzy,
        string? scope,
        IReadOnlyList<CatalogEntry> matches,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext,
        int typeRank,
        Func<string, bool>? pathFilter)
    {
        if (matches.Count == 0)
            return [];

        var normalizedScope = string.IsNullOrEmpty(scope) ? null : SearchResultHelper.NormalizePath(scope);
        var candidates = new List<SearchResultMapper.RankedCandidate>(MaxCandidates);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (!HistorySearchCandidateMapper.TryCreateResult(
                    match.Entry, fuzzy.Text, normalizedScope, fileExists, directoryExists,
                    out var result, out var normalizedPath))
                continue;
            if (pathFilter != null && !pathFilter(normalizedPath))
                continue;
            if (!paths.Add(normalizedPath))
                continue;
            if (rankContext?.PriorityResolver?.Invoke(normalizedPath, result.IsDir).Effect == IndexRuleEffect.Low)
                continue;
            if (HistorySearchCandidateMapper.IsExcludedFromSearch(result))
                continue;

            result.IsLearnedAliasRecall = true;
            var byName = fuzzy.Rank(result.Name);
            var relation = RelationOf(match.Keyword, fuzzy.Text);
            candidates.Add(new SearchResultMapper.RankedCandidate(
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
                Source: RankingCandidateSource.History));
            if (candidates.Count >= MaxCandidates)
                break;
        }
        return candidates;
    }

    private static IReadOnlyList<CatalogEntry> CurrentMatches(string query)
    {
        var version = SearchHistoryStore.AssociationVersion;
        lock (Gate)
        {
            if (_catalog.Version != version)
            {
                _catalog = BuildCatalog(version, SearchHistoryStore.GetLearnedAssociationEntries());
                QueryCache.Clear();
            }

            var now = Environment.TickCount64;
            if (QueryCache.TryGetValue(query, out var cached)
                && now - cached.LastUsed <= CacheLifetimeMs)
            {
                return cached.Matches;
            }

            var matches = MatchCatalog(query, _catalog.Entries, MaxCachedMatches);
            if (QueryCache.Count >= CacheCapacity)
                EvictOldestLocked();
            QueryCache[query] = new CacheEntry { Matches = matches, LastUsed = now };
            return matches;
        }
    }

    private static CatalogSnapshot BuildCatalog(long version, IEnumerable<HistoryEntry> entries)
    {
        var catalog = new List<CatalogEntry>();
        foreach (var entry in entries)
        {
            var keyword = SearchHistoryStore.NormalizeAssociationQuery(entry.Keyword);
            if (keyword.Length == 0)
                continue;
            catalog.Add(new CatalogEntry(entry, keyword, FzfAlgorithm.GetCharMask(keyword)));
        }
        return new CatalogSnapshot(version, catalog);
    }

    internal static IReadOnlyList<CatalogEntry> MatchRelated(
        string query,
        IEnumerable<HistoryEntry> entries,
        int maxMatches) =>
        MatchCatalog(query, BuildCatalog(version: 0, entries).Entries, maxMatches);

    private static IReadOnlyList<CatalogEntry> MatchCatalog(
        string query,
        IReadOnlyList<CatalogEntry> catalog,
        int maxMatches)
    {
        if (!IsPlainQuery(query) || maxMatches <= 0)
            return [];

        var requiredMask = FzfAlgorithm.GetCharMask(query);
        var matches = new List<CatalogEntry>(Math.Min(maxMatches, catalog.Count));
        foreach (var candidate in catalog)
        {
            var relation = RelationOf(candidate.Keyword, query);
            if (relation.Kind == RelationKind.Exact)
                continue;
            if (relation.Kind == RelationKind.None)
                continue;
            if ((candidate.Mask & requiredMask) != requiredMask)
                continue;
            matches.Add(candidate);
        }

        matches.Sort((a, b) =>
        {
            var byRelation = RelationOf(a.Keyword, query).Kind.CompareTo(RelationOf(b.Keyword, query).Kind);
            if (byRelation != 0)
                return byRelation;
            var byTime = b.Entry.Time.CompareTo(a.Entry.Time);
            return byTime != 0 ? byTime : b.Entry.Count.CompareTo(a.Entry.Count);
        });
        if (matches.Count > maxMatches)
            matches.RemoveRange(maxMatches, matches.Count - maxMatches);
        return matches;
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

    private static void EvictOldestLocked()
    {
        string? oldestKey = null;
        var oldest = long.MaxValue;
        foreach (var pair in QueryCache)
        {
            if (pair.Value.LastUsed >= oldest)
                continue;
            oldest = pair.Value.LastUsed;
            oldestKey = pair.Key;
        }
        if (oldestKey != null)
            QueryCache.Remove(oldestKey);
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
