using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
namespace Lertaro.App.Services.Plugin;

/// <summary>
/// Bridges PluginSdk's static service delegates (settings, history, favorites, fuzzy-match,
/// highlight-mask, directory search) to their Core/App implementations. Kept separate from
/// <see cref="PluginManager"/>'s own job (plugin loading, registration, enabled-state filtering)
/// since none of these delegates have anything to do with plugin lifecycle itself.
/// </summary>
internal static class PluginSdkBridge
{
    private static readonly OpenedFolderSnapshotStore OpenedFolderSnapshots = new();

    internal static void ConfigureExplorerPathTracking() =>
        InlineSearchManager.Instance.ExplorerTracker.PathNormalizer = ExplorerPathValidator.NormalizeDirectory;

    internal static void UpdateOpenedFolders(IReadOnlyList<string> paths) => OpenedFolderSnapshots.Update(paths);

    public static void Initialize(PluginManager manager)
    {
        // Wire up the settings delegate for plugins using the in-memory UserSettings cache.
        PluginSdk.Services.PluginSettingsService.GetSettingFunc = manager.GetPluginSetting;
        PluginSdk.Services.PluginSettingsService.SetSettingFunc = manager.SetPluginSetting;
        PluginSdk.Services.UserDataService.GetUserDataDirectoryFunc = () => Logger.UserDataDir;
        PluginSdk.Services.UserDataService.GetSharedDataDirectoryFunc = () => Logger.SharedDataDir;

        // Isolate WebView2 runtime data in <UserDataDir>\FlowData (WebView2 creates FlowData\EBWebView)
        try
        {
            var flowDataDir = System.IO.Path.Combine(Logger.UserDataDir, "FlowData");
            if (!System.IO.Directory.Exists(flowDataDir))
                System.IO.Directory.CreateDirectory(flowDataDir);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", flowDataDir);
        }
        catch { }

        // Wire up the runtime field-prompt delegate, reusing the Settings UI's own field rendering.
        PluginSdk.Services.PluginPromptService.PromptFunc = Views.Controls.Dialogs.PluginFieldPromptWindow.ShowPrompt;

        // Wire up the history service delegate for plugins using Core SearchHistoryStore
        PluginSdk.Services.HistoryService.GetHistoryEntriesFunc = SearchHistoryStore.GetEntries;

        // Wire up the search query modification delegate for plugins
        PluginSdk.Services.SearchQueryService.ChangeQueryFunc = (query, requery) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var quickWindow = System.Windows.Application.Current.Windows.OfType<QuickSearchWindow>().FirstOrDefault(w => w.IsVisible);
                if (quickWindow != null)
                {
                    quickWindow.TxtSearch.Text = query;
                    quickWindow.TxtSearch.CaretIndex = query.Length;
                    quickWindow.TxtSearch.Focus();
                    return;
                }

                var fullWindow = System.Windows.Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault(w => w.IsVisible);
                if (fullWindow != null)
                {
                    fullWindow.SearchTextBox.Text = query;
                    fullWindow.SearchTextBox.CaretIndex = query.Length;
                    fullWindow.SearchTextBox.Focus();
                }
            });

        // Where the user was last browsing, from the same tracker the search context reads. Reached
        // through InlineSearchManager because that is what owns the tracker; it mirrors the Hook
        // process's live state, so this is a field read rather than a round trip.
        PluginSdk.Services.ExplorerPathService.GetLastActivePathFunc =
            () => InlineSearchManager.Instance.ExplorerTracker.LastActiveExplorerPath;
        PluginSdk.Services.ExplorerPathService.GetOpenedFolderPathsFunc = OpenedFolderSnapshots.GetPaths;

        // Wire up the favorites service delegate for plugins using Core UserSettings
        PluginSdk.Services.FavoritesService.GetFavoritesFunc = () =>
            UserSettings.Load().Favorites.Select(f => new PluginSdk.Models.FavoriteItem { Name = f.Name, Path = f.Path });

        // Wire up the fuzzy-match delegate for plugins wanting the host's own matching (with alias
        // fallback) instead of reimplementing a fuzzy matcher of their own
        PluginSdk.Services.FuzzyMatchService.IsMatchFunc = FuzzyMatcher.IsMatch;

        // Wire up the highlight-mask delegate so plugins share the exact same literal/fuzzy/alias
        // highlighting tiers (including CJK pinyin) as the host's own results, instead of each
        // reimplementing a literal-substring-only highlighter that misses fuzzy/alias matches
        PluginSdk.Services.FuzzyMatchService.GetHighlightMaskFunc = FuzzyMatcher.ComputeHighlightMask;

        // Wire up the directory search delegate for plugins using CoreDirectoryIndexManager
        PluginSdk.Services.DirectoryIndexerService.SearchPluginDirectoriesFunc = async (pluginId, query, token) =>
        {
            var results = await CoreDirectoryIndexManager.Instance.SearchPluginDirectoriesAsync(pluginId, query, token).ConfigureAwait(false);
            return results.Select(r => (PluginSdk.Abstractions.ISearchResult)new SimpleSearchResult
            {
                Name = r.Name,
                FullPath = r.Path,
                IsDir = r.IsDir
            }).ToList();
        };

        // Wire up index-backed directory enumeration, so a plugin listing a folder it cares about reads
        // the index the host already maintains instead of hitting the disk itself
        PluginSdk.Services.DirectoryIndexerService.EnumerateDirectoryFunc = EnumerateDirectoryAsync;

        // The same index's recency query. Distinct from enumerating a directory and sorting it, which is
        // what a plugin would otherwise have to do to the whole of Documents to find its newest file.
        PluginSdk.Services.RecentFilesService.GetRecentFilesFunc = GetRecentFilesAsync;

        // Trigger CoreDirectoryIndexManager singleton instantiation to bind SDK DirectoryIndexerService delegates
        _ = CoreDirectoryIndexManager.Instance;
    }

    private static async Task<IReadOnlyList<PluginSdk.Abstractions.ISearchResult>> GetRecentFilesAsync(
        IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken token)
    {
        var recent = await new Core.Services.Search.SearchService()
            .GetRecentFilesAsync(directories, limit, maxAgeMinutes, token).ConfigureAwait(false);

        return recent.Select(result => (PluginSdk.Abstractions.ISearchResult)new SimpleSearchResult
        {
            Name = result.Name,
            FullPath = result.Path,
            IsDir = result.IsDir,
            // The recency query carries Modified and nothing else, which is what the caller sorted by
            // and what a row shows as its "3 minutes ago".
            Metadata = result.Metadata,
        }).ToList();
    }

    private static async IAsyncEnumerable<PluginSdk.Abstractions.ISearchResult> EnumerateDirectoryAsync(
        string directoryPath, bool recursive, string filterPattern, int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        await foreach (var result in CoreDirectoryIndexManager.Instance
            .EnumerateDirectoryAsync(directoryPath, recursive, filterPattern, limit, token).ConfigureAwait(false))
        {
            yield return new SimpleSearchResult
            {
                Name = result.Name,
                FullPath = result.Path,
                IsDir = result.IsDir,
                // Kept from the index rather than dropped: size/date are exactly what a plugin
                // enumerating a folder would otherwise re-stat every entry from disk to learn.
                Metadata = result.Metadata
            };
        }
    }
}
