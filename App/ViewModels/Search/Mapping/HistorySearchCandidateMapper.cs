using System.IO;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Maps the keyword/path pairs already stored by SearchHistoryStore into ordinary App-side candidates.
// It owns no state and deliberately does not alter the index or Core's history persistence behavior.
internal static class HistorySearchCandidateMapper
{
    private const int MaxCandidates = 50;
    public const int MaxLearnedCandidatesPerQuery = 1;

    public static List<SearchResultMapper.RankedCandidate> Collect(FuzzyQuery fuzzy, string? scope, int typeRank = int.MaxValue) =>
        CollectLearned(fuzzy, scope, rankContext: null, typeRank);

    public static List<SearchResultMapper.RankedCandidate> CollectLearned(
        FuzzyQuery fuzzy,
        string? scope,
        RankContext? rankContext,
        int typeRank = int.MaxValue) =>
        CollectLearned(
            fuzzy,
            scope,
            SearchHistoryStore.GetLearnedAssociationEntries(),
            SafeFileExists,
            SafeDirectoryExists,
            rankContext,
            typeRank);

    internal static List<SearchResultMapper.RankedCandidate> CollectLearned(
        FuzzyQuery fuzzy,
        string? scope,
        IEnumerable<HistoryEntry> associationEntries,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        RankContext? rankContext = null,
        int typeRank = int.MaxValue) =>
        CollectCore(
            fuzzy,
            scope,
            associationEntries.Where(entry => entry.Count >= SearchHistoryStore.MinAssociationOrganicOpens),
            fileExists,
            directoryExists,
            typeRank,
            rankContext,
            MaxLearnedCandidatesPerQuery);

    private static bool SafeFileExists(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return SearchReachabilityGate.IsPathReachable(path);
        return File.Exists(path);
    }

    private static bool SafeDirectoryExists(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return SearchReachabilityGate.IsPathReachable(path);
        return Directory.Exists(path);
    }

    internal static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        IEnumerable<HistoryEntry> entries,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        // The caller's rank for the Files type: history rows ARE file paths, so they have to land in the
        // same type group as the index's file rows instead of a group of their own.
        int typeRank = int.MaxValue) =>
        CollectCore(fuzzy, scope, entries, fileExists, directoryExists, typeRank, rankContext: null, MaxCandidates);

    // The association key already passed the exact canonical comparison. Give it a full-strength
    // keyword rank so the history-only marker, not a fuzzy coincidence, decides its tier.
    private static MatchEvidenceResult ExactHistoryMatch(HistoryEntry entry)
    {
        var rank = new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord);
        var evidence = MatchEvidence.Accepted(
            entry.Keyword,
            entry.Keyword,
            MatchEvidenceField.HistoryKeyword,
            rank,
            MatchEvidenceMode.LearnedAlias,
            MatchQueryVariant.HistoryExact);
        return new MatchEvidenceResult(rank, evidence);
    }

    private static List<SearchResultMapper.RankedCandidate> CollectCore(
        FuzzyQuery fuzzy,
        string? scope,
        IEnumerable<HistoryEntry> entries,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        int typeRank,
        RankContext? rankContext,
        int maxCandidates)
    {
        if (fuzzy.IsEmpty)
            return [];

        var query = fuzzy.Text;
        // Learned association is an exact canonical lookup, not another fuzzy surface: the only query
        // allowed to recall a learned pair is the one the user confirmed, after P4's pinyin-equivalence
        // folding (see SearchHistoryStore.NormalizeAssociationQuery).
        var canonicalQuery = SearchHistoryStore.NormalizeAssociationQuery(query);
        var matches = entries
            .Where(entry => canonicalQuery.Length > 0
                && string.Equals(
                    SearchHistoryStore.NormalizeAssociationQuery(entry.Keyword),
                    canonicalQuery,
                    StringComparison.OrdinalIgnoreCase))
            .Select(entry => (Entry: entry, Matched: ExactHistoryMatch(entry)))
            .OrderBy(candidate => candidate.Matched.Rank.Start)
            // Most recent pick first: choosing the 10th result for "atlas" makes it the top one next time,
            // even over something opened more often before.
            .ThenByDescending(candidate => candidate.Entry.Time)
            .ThenByDescending(candidate => candidate.Entry.Count)
            // Shape is gone; the shared quality ladder is the only textual order.
            .ThenBy(candidate => candidate.Matched.Rank, MatchRankQualityComparer.Instance);
        var candidates = new List<SearchResultMapper.RankedCandidate>(Math.Min(maxCandidates, MaxCandidates));
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedScope = string.IsNullOrEmpty(scope) ? null : SearchResultHelper.NormalizePath(scope);

        foreach (var (entry, matched) in matches)
        {
            if (candidates.Count >= maxCandidates)
                break;
            if (!TryCreateResult(entry, query, normalizedScope, fileExists, directoryExists, out var result, out var normalizedPath) ||
                !candidatePaths.Add(normalizedPath))
                continue;
            if (rankContext?.PriorityResolver?.Invoke(normalizedPath, result.IsDir).Effect == IndexRuleEffect.Low)
                continue;
            if (IsExcludedFromSearch(result))
                continue;
            result.IsLearnedAliasRecall = true;

            // The row is marked history-only: the keyword matched, not the displayed name, so the
            // quality ladder must not treat its perfect keyword rank as a name hit. Its open count feeds the
            // decayed-score budget instead of a separate priority signal.
            candidates.Add(new SearchResultMapper.RankedCandidate(
                result,
                IsFavorite: false,
                HistoryCount: entry.Count,
                TypeRank: typeRank,
                Match: matched.Rank,
                NormalizedPath: normalizedPath,
                IsHistoryOnly: true,
                Source: RankingCandidateSource.History,
                Evidence: matched.Evidence with
                {
                    Mode = MatchEvidenceMode.LearnedAlias,
                    QueryVariant = MatchQueryVariant.HistoryExact,
                }));
        }

        return candidates;
    }

    // The two exclusions the index side applies to every row IT returns, applied here to the rows this
    // window can produce on its own (history, Windows' Recent Items, favorites):
    //
    //   * hidden/system entries -- SearchService drops them from index results (FileSystemItemFilter);
    //   * a source this session found unreachable -- SearchStreamRenderer drops those rows (SearchReachabilityGate).
    //
    // Without this, a hidden file or one on a disabled drive was listed by the quick window and could never
    // appear in the full window, which reads as "the same query works in one window and not the other". Being
    // reachable-by-this-window-only is not supposed to mean "found only here": the row still has to clear the
    // bar the index side holds its rows to.
    //
    // Split from the convenience overload below so the policy itself is testable without a session probe or a
    // real hidden file.
    internal static bool IsExcludedFromSearch(AppSearchResult row, IReadOnlySet<string> unreachable, Func<string, bool> isHiddenOrSystem)
        => !SearchReachabilityGate.IsPathReachable(row.FullPath, unreachable) || IsHiddenSystemLocalPath(row.FullPath, isHiddenOrSystem);

    internal static bool IsExcludedFromSearch(AppSearchResult row)
        => IsExcludedFromSearch(row, SearchReachabilityGate.UnreachableSources, FileSystemItemFilter.IsHiddenOrSystem);

    // The attribute half is only ever asked of a LOCAL path. File.GetAttributes has no timeout, and this runs
    // while a search paints -- on a share that is gone it is the same multi-second stall SafeFileExists above
    // exists to avoid, and for a path this machine has no attributes for the index side would not have had a
    // row either way. The drive-letter test is SearchReachabilityGate's own.
    private static bool IsHiddenSystemLocalPath(string path, Func<string, bool> isHiddenOrSystem)
        => path.Length > 2 && path[1] == ':' && isHiddenOrSystem(path);

    // Also builds WindowsRecentCandidateMapper's rows: a Recent Items hit is presented exactly like a
    // history entry for the same path.
    internal static bool TryCreateResult(
        HistoryEntry entry,
        string query,
        string? normalizedScope,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        out AppSearchResult result,
        out string normalizedPath)
    {
        result = null!;
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(entry.Path) || !Path.IsPathFullyQualified(entry.Path))
            return false;

        try
        {
            var rawPath = entry.Path.Trim().Trim('"');
            var path = WslPath.IsPath(rawPath)
                ? rawPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                : Path.GetFullPath(rawPath);
            var isDirectory = entry.Kind == HistoryEntryKind.Folder;
            if (!(isDirectory ? directoryExists(path) : fileExists(path)))
                return false;

            normalizedPath = SearchResultHelper.NormalizePath(path);
            if (normalizedScope != null &&
                (!SearchResultHelper.IsPathInsideScope(normalizedPath, normalizedScope) ||
                 string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (entry.Kind == HistoryEntryKind.Application &&
                SearchableItemMapper.TryCreateApplicationResult(path, query, out result))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmedPath);
            var isApplication = entry.Kind == HistoryEntryKind.Application;
            result = new AppSearchResult
            {
                // A removed/unloaded app no longer has a provider item to reuse above. Keep the same
                // application presentation in that case, including the title-without-extension rule.
                Name = string.IsNullOrEmpty(name) ? path : (isApplication ? Path.GetFileNameWithoutExtension(name) : name),
                FullPath = path,
                ParentDir = isApplication ? string.Empty : parent,
                ContextDirectory = isDirectory ? path : parent,
                IsDir = isDirectory,
                Drive = Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(':') ?? string.Empty,
                ResultKind = isApplication ? "Application" : "File",
                InstantResultOnExecute = isApplication ? () => FileExecutor.OpenFileOrFolder(path) : null,
                InstantResultActionArgument = isApplication ? path : string.Empty,
                SearchQuery = query
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static List<AppSearchResult> MergeRows(
        IReadOnlyList<SearchResultMapper.RankedCandidate> learned,
        IReadOnlyList<AppSearchResult> ordinary,
        int limit = MaxCandidates)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<AppSearchResult>(Math.Min(limit, learned.Count + ordinary.Count));

        foreach (var candidate in learned)
        {
            if (merged.Count >= limit)
                break;
            if (paths.Add(candidate.NormalizedPath))
                merged.Add(candidate.Result);
        }

        foreach (var result in ordinary)
        {
            if (merged.Count >= limit)
                break;
            if (paths.Add(SearchResultHelper.NormalizePath(result.FullPath)))
                merged.Add(result);
        }

        for (var index = 0; index < merged.Count; index++)
            merged[index].Index = index;
        return merged;
    }
}
