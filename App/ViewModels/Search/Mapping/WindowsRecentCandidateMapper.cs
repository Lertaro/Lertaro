using System.IO;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Windows' own Recent Items (%AppData%\Microsoft\Windows\Recent): the shell drops a shortcut there whenever
// a document or folder is opened in any app, Lertaro or not. Matching hits rank below the user's Lertaro
// picks (history and favorites, which are curated) and above every ordinary result -- better match shape
// first, then most recently opened.
//
// Each shortcut is named after its target ("code-atlas.lnk", "report.docx.lnk"), so the query is matched
// against that name and only a hit pays for the COM call that resolves it.
internal static class WindowsRecentCandidateMapper
{
    private const int MaxCandidates = 20;

    // ponytail: the folder listing is cached until the folder changes or this much time passes. Reopening
    // a document the shell already lists rewrites its shortcut without touching the folder, so that
    // reorder can lag by up to this long. Upgrade path: a FileSystemWatcher on the folder.
    private static readonly TimeSpan ListingTtl = TimeSpan.FromSeconds(30);

    private static readonly object Gate = new();
    private static (DateTime FolderWriteUtc, DateTime LoadedUtc, Shortcut[] Items)? _listing;
    // ponytail: one entry per shortcut path resolved, never evicted -- bounded by the Recent folder's own
    // churn over this process's lifetime (a few thousand short strings at worst, since only shortcuts a
    // query actually matched are ever resolved). Upgrade path: drop entries whose .lnk is gone when the
    // listing is refreshed.
    private static readonly Dictionary<string, (DateTime WriteUtc, string? Target)> Targets = new(StringComparer.OrdinalIgnoreCase);

    internal readonly record struct Shortcut(string Path, string TargetName, DateTime LastWriteUtc);

    // Swapped out by the App test assembly, so tests that build whole result lists never pick up whatever
    // the machine running them happens to have opened recently.
    internal static Func<IReadOnlyList<Shortcut>> Source = Listing;

    public static List<SearchResultMapper.RankedCandidate> Collect(FuzzyQuery fuzzy, string? scope, int typeRank = int.MaxValue) =>
        fuzzy.IsEmpty ? [] : Collect(fuzzy, scope, Source(), ResolveCached, SafeFileExists, SafeDirectoryExists, typeRank);

    // For ranking rows the index already returned (the full window): those exist by construction, so the
    // targets are not probed.
    public static List<SearchResultMapper.RankedCandidate> CollectForRanking(FuzzyQuery fuzzy) =>
        fuzzy.IsEmpty ? [] : Collect(fuzzy, null, Source(), ResolveCached, _ => true, _ => true);

    internal static List<SearchResultMapper.RankedCandidate> Collect(
        FuzzyQuery fuzzy,
        string? scope,
        IReadOnlyList<Shortcut> shortcuts,
        Func<Shortcut, string?> resolve,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        int typeRank = int.MaxValue)
    {
        if (fuzzy.IsEmpty || shortcuts.Count == 0)
            return [];

        var hits = shortcuts
            .Select(shortcut => (Shortcut: shortcut, Matched: fuzzy.BestMatchWithEvidence(
                shortcut.TargetName,
                MatchEvidenceField.FileName)))
            .Where(hit => hit.Matched.Rank.IsMatch)
            .OrderBy(hit => hit.Matched.Rank, MatchRankQualityComparer.Instance)
            .ThenByDescending(hit => hit.Shortcut.LastWriteUtc);
        var normalizedScope = string.IsNullOrEmpty(scope) ? null : SearchResultHelper.NormalizePath(scope);
        var candidates = new List<SearchResultMapper.RankedCandidate>(MaxCandidates);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (shortcut, matched) in hits)
        {
            if (candidates.Count >= MaxCandidates)
                break;

            // A shortcut to something deleted, renamed or on an unplugged drive resolves to nothing, or
            // TryCreateResult's existence check drops it -- the shell leaves those behind for weeks.
            var target = resolve(shortcut);
            if (string.IsNullOrWhiteSpace(target))
                continue;
            var kind = directoryExists(target) ? HistoryEntryKind.Folder : HistoryEntryKind.File;
            if (!HistorySearchCandidateMapper.TryCreateResult(
                    new HistoryEntry(string.Empty, target, kind, 0), fuzzy.Text, normalizedScope,
                    fileExists, directoryExists, out var result, out var normalizedPath) ||
                !paths.Add(normalizedPath) ||
                // Same bar the history rows clear, and the same one the index side holds its rows to: a
                // Recent Items shortcut to something hidden or on this session's unreachable drive is not a
                // row the full window could ever list either (see HistorySearchCandidateMapper).
                HistorySearchCandidateMapper.IsExcludedFromSearch(result))
                continue;

            // Not harvested by Lertaro: no open count and no decayed score, so this row never earns a
            // history tier. It competes on its name match alone.
            candidates.Add(new SearchResultMapper.RankedCandidate(
                result,
                IsFavorite: false,
                HistoryCount: 0,
                TypeRank: typeRank,
                Match: matched.Rank,
                NormalizedPath: normalizedPath,
                Source: RankingCandidateSource.WindowsRecent,
                Evidence: matched.Evidence));
        }

        return candidates;
    }

    private static IReadOnlyList<Shortcut> Listing()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (string.IsNullOrEmpty(folder))
            return [];

        try
        {
            var directory = new DirectoryInfo(folder);
            if (!directory.Exists)
                return [];
            var folderWrite = directory.LastWriteTimeUtc;
            lock (Gate)
            {
                if (_listing is { } cached && cached.FolderWriteUtc == folderWrite && DateTime.UtcNow - cached.LoadedUtc < ListingTtl)
                    return cached.Items;
            }

            var items = directory.EnumerateFiles("*.lnk")
                .Select(file => new Shortcut(file.FullName, Path.GetFileNameWithoutExtension(file.Name), file.LastWriteTimeUtc))
                .ToArray();
            lock (Gate)
                _listing = (folderWrite, DateTime.UtcNow, items);
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Logger.Log($"[WindowsRecentCandidateMapper] Cannot read {folder}: {ex.Message}", LogLevel.Warn);
            return [];
        }
    }

    // The same shortcut is resolved on every keystroke that matches it; its target only changes when the
    // shell rewrites the shortcut.
    private static string? ResolveCached(Shortcut shortcut)
    {
        lock (Gate)
        {
            if (Targets.TryGetValue(shortcut.Path, out var known) && known.WriteUtc == shortcut.LastWriteUtc)
                return known.Target;
        }

        var target = StartMenuShortcutResolver.ResolveShortcutTarget(shortcut.Path);
        lock (Gate)
            Targets[shortcut.Path] = (shortcut.LastWriteUtc, target);
        return target;
    }

    // A network path is probed through the reachability gate first: File/Directory.Exists have no timeout
    // and can block for seconds on a share that is gone, and this runs while a search paints.
    private static bool SafeFileExists(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
            ? SearchReachabilityGate.IsPathReachable(path) && File.Exists(path)
            : File.Exists(path);

    private static bool SafeDirectoryExists(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal)
            ? SearchReachabilityGate.IsPathReachable(path) && Directory.Exists(path)
            : Directory.Exists(path);
}
