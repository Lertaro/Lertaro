using System.IO;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

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
    public bool AcceptsDrops => Selected?.Id.Equals(QuickLaunchSourceCatalog.ManualSourceId, StringComparison.OrdinalIgnoreCase) == true;

    public async Task RefreshAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        var selectedId = Selected?.Id;
        Sources.ReplaceRange(Array.Empty<LaunchPanelSourceViewModel>());
        Selected = null;
        _notify(nameof(QuickSearchViewModel.LaunchPanelVisibility));
        _notify(nameof(QuickSearchViewModel.LaunchPanelItems));
        _notify(nameof(QuickSearchViewModel.LaunchPanelHeight));
        _notify(nameof(QuickSearchViewModel.HasMultipleLaunchSources));
        _notify(nameof(QuickSearchViewModel.CanAcceptLaunchPanelDrops));
        var settings = UserSettings.Load().QuickLaunch;
        var loaded = new List<LaunchPanelSourceViewModel>();

        if (settings.Enabled && settings.Items.Count > 0)
        {
            var items = settings.Items
                .Where(item => FavoritePathResolver.IsPathAvailable(item.Path))
                .Select((item, index) => LaunchItemMapper.ToUiResult(item, index))
                .ToList();
            if (items.Count > 0)
                loaded.Add(new LaunchPanelSourceViewModel(QuickLaunchSourceCatalog.ManualSourceId,
                    TranslationManager.Instance["QuickLaunch_ManualSource"], items));
        }

        if (settings.Enabled)
        {
            var providers = QuickLaunchSourceCatalog.GetEnabledSourceIds(settings)
                .Select(QuickLaunchSourceCatalog.Find)
                .Where(provider => provider != null)
                .Cast<IQuickPanelTabProvider>()
                .ToList();
            var providerResults = await Task.WhenAll(providers.Select(provider => LoadProviderAsync(provider, token)));
            for (var i = 0; i < providers.Count; i++)
            {
                if (providerResults[i].Count == 0) continue;
                var items = providerResults[i]
                    .Select((item, index) => PluginResultMapper.ToUiResult(item, index))
                    .ToList();
                loaded.Add(new LaunchPanelSourceViewModel(QuickLaunchSourceCatalog.GetId(providers[i]), providers[i].Name, items));
            }
        }

        if (token.IsCancellationRequested) return;
        var ordered = QuickLaunchSourceCatalog.OrderSources(loaded, settings.SourceOrder);
        Sources.ReplaceRange(ordered);
        Selected = ordered.FirstOrDefault(source => source.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            ?? ordered.FirstOrDefault();
        UpdateSelection();
        _notify(nameof(QuickSearchViewModel.LaunchPanelVisibility));
        _notify(nameof(QuickSearchViewModel.LaunchPanelItems));
        _notify(nameof(QuickSearchViewModel.LaunchPanelHeight));
        _notify(nameof(QuickSearchViewModel.HasMultipleLaunchSources));
        _notify(nameof(QuickSearchViewModel.CanAcceptLaunchPanelDrops));
    }

    public void Select(LaunchPanelSourceViewModel? source)
    {
        if (source == null || !Sources.Contains(source)) return;
        Selected = source;
        UpdateSelection();
        _notify(nameof(QuickSearchViewModel.LaunchPanelItems));
        _notify(nameof(QuickSearchViewModel.CanAcceptLaunchPanelDrops));
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

    private static async Task<IReadOnlyList<ISearchResult>> LoadProviderAsync(
        IQuickPanelTabProvider provider, CancellationToken token)
    {
        try
        {
            return await provider.GetEntriesAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Array.Empty<ISearchResult>();
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickLaunch] Failed to load source '{provider.GetType().Name}': {ex.Message}", LogLevel.Warn);
            return Array.Empty<ISearchResult>();
        }
    }
}
