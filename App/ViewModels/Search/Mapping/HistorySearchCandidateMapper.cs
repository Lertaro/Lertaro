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

    public static List<SearchResultMapper.RankedCandidate> Collect(FuzzyQuery fuzzy, string? scope) =>
        Collect(fuzzy, scope, SearchHistoryStore.GetEntries(), SafeFileExists, SafeDirectoryExists);

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
        Func<string, bool> directoryExists)
    {
        if (fuzzy.IsEmpty)
            return [];

        var query = fuzzy.Text;
        var matches = entries
            .Select(entry => (Entry: entry, Match: fuzzy.BestMatch(entry.Keyword)))
            .Where(candidate => candidate.Match.IsMatch)
            .OrderBy(candidate => candidate.Match.Start)
            .ThenByDescending(candidate => candidate.Entry.Count)
            .ThenByDescending(candidate => candidate.Entry.Time)
            .ThenByDescending(candidate => candidate.Match.Weight);
        var candidates = new List<SearchResultMapper.RankedCandidate>(MaxCandidates);
        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedScope = string.IsNullOrEmpty(scope) ? null : SearchResultHelper.NormalizePath(scope);

        foreach (var (entry, match) in matches)
        {
            if (candidates.Count >= MaxCandidates)
                break;
            if (!TryCreateResult(entry, query, normalizedScope, fileExists, directoryExists, out var result, out var normalizedPath) ||
                !candidatePaths.Add(normalizedPath))
                continue;

            // Negative priorities put a learned keyword match ahead of ordinary global history, whose
            // priorities start at zero. Match position, usage count, recency, and match quality determine
            // this list's order above.
            candidates.Add(new SearchResultMapper.RankedCandidate(
                result,
                IsCurated: true,
                Priority: candidates.Count - MaxCandidates,
                TypeRank: int.MaxValue,
                Match: match,
                NormalizedPath: normalizedPath));
        }

        return candidates;
    }

    private static bool TryCreateResult(
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

    internal static IReadOnlyDictionary<string, int> ApplyPriorities(
        IReadOnlyDictionary<string, int> existing,
        IReadOnlyList<SearchResultMapper.RankedCandidate> learned)
    {
        var priorities = existing.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in learned)
        {
            var path = candidate.Result.FullPath;
            if (path.Length > 3 && path[^1] == '\\')
                path = path.TrimEnd('\\');
            if (!priorities.TryGetValue(path, out var priority) || candidate.Priority < priority)
                priorities[path] = candidate.Priority;
        }
        return priorities;
    }
}
