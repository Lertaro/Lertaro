using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Search;

// Keeps the per-folder search fan-out out of SearchStreamRenderer so each file stays below the
// repository's line limit. The runner owns only orchestration; SearchService still owns all index work.
internal static class ScopedSearchRunner
{
    public const string ShortcutSupplementFilter = "*.lnk;*.url";

    public static bool ShouldRunShortcutSupplement(
        int fileLimit,
        bool foldersOnly,
        string? searchScope,
        FileFilterScopeDirective? scopeDirective) =>
        fileLimit < int.MaxValue
        && !foldersOnly
        && string.IsNullOrEmpty(searchScope)
        && scopeDirective is not { Folders.Count: > 0 };

    public static async Task RunAsync(
        SearchService searchService,
        FileFilterScopeDirective directive,
        string query,
        int fileLimit,
        int appLimit,
        Action<SearchResult> onResult,
        Action onLocalSearchFailed,
        bool bypassExclusions,
        CancellationToken token)
    {
        var failureReported = 0;
        void ReportLocalSearchFailedOnce()
        {
            if (Interlocked.Exchange(ref failureReported, 1) == 0)
                onLocalSearchFailed();
        }

        var tasks = directive.Folders.Select(folder => RunFolderAsync(
            searchService, folder, query, fileLimit, appLimit, onResult,
            ReportLocalSearchFailedOnce, bypassExclusions, directive.FilterPattern, token)).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded or window-closed: scoped searches stop silently like unscoped searches.
        }
    }

    /// <summary>
    /// Searches each high-priority directory on its own and adds what it finds to the same result stream.
    /// </summary>
    /// <remarks>
    /// The engine truncates the shared candidate pool by name relevance before the App can apply priority,
    /// so a promoted folder with ordinary names ("Readme.txt") could never reach the ranking. These rows are
    /// additive and unordered; the App re-ranks the merged set, where the priority rules put them first.
    /// Failures are swallowed per folder: a broken rule must not cost the query its normal results.
    /// </remarks>
    public static async Task RunPriorityRootsAsync(
        SearchService searchService,
        IReadOnlyList<string> roots,
        string query,
        int perRootLimit,
        int appLimit,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        var tasks = roots.Select(root => RunFolderAsync(
            searchService, root, query, perRootLimit, appLimit, onResult,
            onLocalSearchFailed: static () => { }, bypassExclusions: false, fileNameFilter: null, token)).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Same rule as the folder fan-out above.
        }
    }

    /// <summary>
    /// Ensures top-ranked <c>.lnk</c>/<c>.url</c> shortcuts across every drive reach bounded quick/inline
    /// windows even when a short initials query matches hundreds of non-shortcut files first (#507).
    /// </summary>
    public static async Task RunShortcutSupplementAsync(
        SearchService searchService,
        string query,
        int shortcutLimit,
        int appLimit,
        Action<SearchResult> onResult,
        CancellationToken token)
    {
        try
        {
            await searchService.SearchStreamingAsync(
                query,
                shortcutLimit,
                appLimit,
                directoryFilter: null,
                onResult,
                token,
                onLocalSearchFailed: static () => { },
                bypassExclusions: false,
                fileNameFilter: ShortcutSupplementFilter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Same cancellation contract as priority roots above.
        }
        catch (Exception ex)
        {
            Logger.Log($"[ScopedSearchRunner] Shortcut supplement search failed: {ex.Message}", LogLevel.Warn);
        }
    }

    private static async Task RunFolderAsync(
        SearchService searchService,
        string folder,
        string query,
        int fileLimit,
        int appLimit,
        Action<SearchResult> onResult,
        Action onLocalSearchFailed,
        bool bypassExclusions,
        string? fileNameFilter,
        CancellationToken token)
    {
        try
        {
            await searchService.SearchStreamingAsync(
                query, fileLimit, appLimit, folder, onResult, token,
                onLocalSearchFailed, bypassExclusions, fileNameFilter: fileNameFilter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ScopedSearchRunner] Search of '{folder}' failed: {ex.Message}", LogLevel.Warn);
        }
    }
}
