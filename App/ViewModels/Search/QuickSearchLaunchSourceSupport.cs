using System.IO;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.ViewModels.Search;

// Split out from QuickSearchViewModel to keep the view model focused on search bindings and under the
// repository's per-file line limit. This support owns only the transient source loading state for its
// one QuickSearchViewModel owner.
internal sealed class QuickSearchLaunchSourceSupport
{
    private readonly Action<string> _notify;
    private CancellationTokenSource? _loadCancellation;

    public QuickSearchLaunchSourceSupport(Action<string> notify) => _notify = notify;

    public ObservableRangeCollection<LaunchPanelSourceViewModel> Sources { get; } = new();
    public LaunchPanelSourceViewModel? Selected { get; private set; }
    public AppSearchResult? SelectedItem { get; private set; }
    public bool AcceptsDrops => Selected?.Id.Equals(QuickLaunchSourceCatalog.ManualSourceId, StringComparison.OrdinalIgnoreCase) == true;
    public bool IsManualSourceSelected => AcceptsDrops;

    public async Task RefreshAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        var selectedId = Selected?.Id;

        // Deliberately NOT emptied first. The panel used to be blanked for the whole load, so the last
        // summon's tiles disappeared and nothing replaced them until every source had answered -- which is
        // the wait the summon showed. What is on screen stays on screen until the new answer is in.
        var loaded = await ReadSourcesAsync(token);
        if (loaded == null) return;

        var settings = UserSettings.Load().QuickLaunch;
        var ordered = QuickLaunchSourceCatalog.OrderSources(loaded, settings.SourceOrder);
        Sources.ReplaceRange(ordered);
        Selected = ordered.FirstOrDefault(source => source.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault();
        UpdateSelection();
        // Every row is rebuilt, so the remembered one is no longer an instance any source holds.
        SelectedItem = null;
        _notify(nameof(QuickSearchViewModel.LaunchPanelVisibility));
        _notify(nameof(QuickSearchViewModel.LaunchPanelItems));
        _notify(nameof(QuickSearchViewModel.SelectedLaunchPanelItem));
        _notify(nameof(QuickSearchViewModel.LaunchPanelHeight));
        _notify(nameof(QuickSearchViewModel.LaunchPanelItemScale));
        _notify(nameof(QuickSearchViewModel.HasMultipleLaunchSources));
        _notify(nameof(QuickSearchViewModel.CanAcceptLaunchPanelDrops));
        _notify(nameof(QuickSearchViewModel.IsManualLaunchSourceSelected));
    }

    /// <summary>Everything the strip is about to show, or null when a newer refresh superseded this one.</summary>
    /// <remarks>
    /// Starts and finishes on the UI thread on purpose, and awaits back onto it: what it builds are bound
    /// rows, translation lookups and (for a web-address entry) an ImageSource. Only the part that can block
    /// for seconds -- asking whether a configured path still exists -- is handed to another thread.
    /// </remarks>
    private async Task<List<LaunchPanelSourceViewModel>?> ReadSourcesAsync(CancellationToken token)
    {
        var settings = UserSettings.Load().QuickLaunch;
        var loaded = new List<LaunchPanelSourceViewModel>();

        if (settings.Enabled && settings.Items.Count > 0)
        {
            // Every configured path is asked whether it is still there, and one on a disconnected share
            // answers by timing out. Budgeted and off this thread for exactly that reason: this used to run
            // on the UI thread and every dead path in turn held the summon behind its own timeout.
            var probes = settings.Items
                .Select(item => (Func<bool>)(() => FavoritePathResolver.IsPathAvailable(item.Path)))
                .ToList();
            var available = await Task.Run(
                () => BudgetedProbeBatch.Run(probes, false, BudgetedProbeBatch.DefaultBudget), token);

            var items = settings.Items
                .Where((_, index) => available[index])
                .Select((item, index) => LaunchItemMapper.ToUiResult(item, index))
                .ToList();
            if (items.Count > 0)
            {
                var manualSource = new LaunchPanelSourceViewModel(QuickLaunchSourceCatalog.ManualSourceId,
                    TranslationManager.Instance["QuickLaunch_ManualSource"], items);
                manualSource.Items.CollectionChanged += (_, _) => SaveManualItemOrder(manualSource.Items);
                loaded.Add(manualSource);
            }
        }

        if (!settings.Enabled) return token.IsCancellationRequested ? null : loaded;

        var providers = QuickLaunchSourceCatalog.GetEnabledSourceIds(settings)
            .Select(QuickLaunchSourceCatalog.Find)
            .Where(provider => provider != null)
            .Cast<IQuickPanelTabProvider>()
            .ToList();
        var providerResults = await Task.WhenAll(
            providers.Select(provider =>
                QuickLaunchSourceReader.LoadProviderAsync(QuickLaunchSourceCatalog.GetId(provider), provider, token)));
        if (token.IsCancellationRequested) return null;

        for (var i = 0; i < providers.Count; i++)
        {
            if (providerResults[i].Count == 0) continue;
            var items = providerResults[i]
                .Select((item, index) => PluginResultMapper.ToUiResult(item, index))
                .ToList();
            loaded.Add(new LaunchPanelSourceViewModel(QuickLaunchSourceCatalog.GetId(providers[i]), providers[i].Name, items));
        }

        return loaded;
    }

    public void Select(LaunchPanelSourceViewModel? source)
    {
        if (source == null || !Sources.Contains(source)) return;
        Selected = source;
        SelectedItem = null;
        UpdateSelection();
        _notify(nameof(QuickSearchViewModel.LaunchPanelItems));
        _notify(nameof(QuickSearchViewModel.SelectedLaunchPanelItem));
        _notify(nameof(QuickSearchViewModel.CanAcceptLaunchPanelDrops));
        _notify(nameof(QuickSearchViewModel.IsManualLaunchSourceSelected));
    }

    public void SelectItem(AppSearchResult? item)
    {
        if (item != null && (Selected == null || !Selected.Items.Contains(item)))
            return;
        if (ReferenceEquals(SelectedItem, item))
            return;

        SelectedItem = item;
        _notify(nameof(QuickSearchViewModel.SelectedLaunchPanelItem));
    }

    public bool MoveItemSelection(int rowDelta, int columnDelta, int columns)
    {
        var items = Selected?.Items;
        if (items == null || items.Count == 0)
            return false;

        var next = NextGridSelection(
            SelectedItem == null ? -1 : items.IndexOf(SelectedItem), rowDelta, columnDelta, items.Count, columns);
        if (next < 0)
            return false;

        SelectItem(items[next]);
        return true;
    }

    /// <summary>Returns the item reached by a visual grid move, or -1 at a panel edge.</summary>
    /// <remarks>
    /// UniformGrid lays the launch items out row-major, so its column count is enough to resolve a
    /// visual move without inspecting WPF elements. Vertical moves keep the current column and stop if
    /// the target row has no item in that column. Deliberately does not wrap: reaching an edge should
    /// leave the selection where the user can see it.
    /// </remarks>
    internal static int NextGridSelection(int currentIndex, int rowDelta, int columnDelta, int itemCount, int columns)
    {
        if (itemCount <= 0 || (rowDelta == 0 && columnDelta == 0))
            return -1;

        columns = Math.Max(1, columns);
        if (currentIndex < 0 || currentIndex >= itemCount)
            return rowDelta < 0 ? itemCount - 1 : 0;

        var row = currentIndex / columns;
        var column = currentIndex % columns;
        if (rowDelta != 0)
        {
            var targetRow = row + rowDelta;
            var targetStart = targetRow * columns;
            if (targetRow < 0 || targetStart < 0 || targetStart >= itemCount)
                return -1;

            var target = targetStart + column;
            return target < itemCount ? target : -1;
        }

        // Horizontal movement follows the visible reading order across row boundaries: the item after
        // the last column is the first item on the next row, and vice versa. Unlike vertical movement,
        // this never invents a position in a short final row.
        var targetIndex = currentIndex + columnDelta;
        return targetIndex >= 0 && targetIndex < itemCount ? targetIndex : -1;
    }

    public bool RemoveManualItem(AppSearchResult result)
    {
        var userSettings = UserSettings.Load();
        var settings = userSettings.QuickLaunch;
        var index = settings.Items.FindIndex(item => PathsMatch(item.Path, result.FullPath));
        if (index < 0)
            return false;

        settings.Items.RemoveAt(index);
        userSettings.Save();
        _ = RefreshAsync();
        return true;
    }

    public int AddDroppedPaths(IEnumerable<string> paths)
    {
        if (!AcceptsDrops)
            return 0;

        var userSettings = UserSettings.Load();
        var settings = userSettings.QuickLaunch;
        var existing = settings.Items.ToList();
        var existingPaths = existing
            .Select(item => FavoritePathResolver.NormalizeForComparison(item.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var rawPath in paths)
        {
            var path = rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
                continue;

            var comparison = FavoritePathResolver.NormalizeForComparison(path);
            if (!existingPaths.Add(comparison))
                continue;

            var name = LaunchItemNameHelper.GetAutomaticName(path);
            existing.Add(new QuickLaunchItemSetting { Name = name, Path = path });
            added++;
        }

        if (added == 0)
            return 0;

        settings.Items = existing;
        userSettings.Save();
        _ = RefreshAsync();
        return added;
    }

    public void Cycle(int direction)
    {
        if (Sources.Count == 0) return;
        var index = Selected == null ? 0 : Sources.IndexOf(Selected);
        index = (index + direction % Sources.Count + Sources.Count) % Sources.Count;
        Select(Sources[index]);
    }

    private void UpdateSelection()
    {
        foreach (var source in Sources)
            source.IsSelected = ReferenceEquals(source, Selected);
    }

    // internal rather than private: the manual source the reader builds is the one this subscribes to, so
    // the handler has to be reachable from there.
    internal static void SaveManualItemOrder(IReadOnlyList<AppSearchResult> displayedItems)
    {
        var userSettings = UserSettings.Load();
        var settings = userSettings.QuickLaunch;
        var ordered = OrderManualItems(settings.Items, displayedItems);

        if (ordered.SequenceEqual(settings.Items))
            return;

        settings.Items = ordered;
        userSettings.Save();
    }

    internal static List<QuickLaunchItemSetting> OrderManualItems(
        IReadOnlyList<QuickLaunchItemSetting> configuredItems,
        IReadOnlyList<AppSearchResult> displayedItems)
    {
        var ordered = new List<QuickLaunchItemSetting>(configuredItems.Count);
        var used = new HashSet<QuickLaunchItemSetting>();

        foreach (var displayed in displayedItems)
        {
            var item = configuredItems.FirstOrDefault(candidate =>
                !used.Contains(candidate) && PathsMatch(candidate.Path, displayed.FullPath));
            if (item == null)
                continue;

            used.Add(item);
            ordered.Add(item);
        }

        ordered.AddRange(configuredItems.Where(item => !used.Contains(item)));
        return ordered;
    }

    private static bool PathsMatch(string configuredPath, string displayedPath)
        => string.Equals(
            FavoritePathResolver.NormalizeForComparison(FavoritePathResolver.Resolve(configuredPath)),
            FavoritePathResolver.NormalizeForComparison(displayedPath),
            StringComparison.OrdinalIgnoreCase);
}
