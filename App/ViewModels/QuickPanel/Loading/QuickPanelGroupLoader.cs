using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel.Loading;

// Split from QuickPanelViewModelLoading to keep the view-model files under the repository line limit.
// It owns only one source's progressive load; the view model retains tab placement and visible state.
internal sealed class QuickPanelGroupLoader
{
    private readonly Func<QuickPanelFolderSource, IProgress<IReadOnlyList<SearchResult>>, CancellationToken, Task<List<SearchResult>>> _load;
    private readonly bool _mapOnBackground;

    public QuickPanelGroupLoader(
        Func<QuickPanelFolderSource, IProgress<IReadOnlyList<SearchResult>>, CancellationToken, Task<List<SearchResult>>> load,
        bool mapOnBackground)
    {
        _load = load;
        _mapOnBackground = mapOnBackground;
    }

    public async Task LoadAsync(
        QuickPanelTab workspace,
        QuickPanelFolderSource source,
        Action<QuickPanelGroupViewModel> place,
        CancellationToken token)
    {
        workspace.GroupPreferences.TryGetValue(source.Id, out var preference);
        QuickPanelGroupViewModel? group = null;
        var acceptingProgress = true;
        var progress = new Progress<IReadOnlyList<SearchResult>>(batch =>
        {
            if (!acceptingProgress)
                return;

            var items = Map(batch);
            if (items.Count == 0)
                return;

            if (group == null)
            {
                group = Create(source, preference, items, isLoading: true);
                place(group);
            }
            else
            {
                group.AppendLoading(items);
            }
        });

        List<SearchResult> results;
        try
        {
            results = await _load(source, progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickPanel] Source '{source.Path}' failed to load: {ex.Message}", LogLevel.Error);
            return;
        }

        acceptingProgress = false;
        var completeItems = _mapOnBackground
            ? await Task.Run(() => Map(results), token).ConfigureAwait(true)
            : Map(results);
        if (group == null)
        {
            if (completeItems.Count == 0)
                return;

            group = Create(source, preference, completeItems, isLoading: false);
            place(group);
            return;
        }

        group.Replace(completeItems);
    }

    internal static List<(AppSearchResult Item, DateTime? Modified)> Map(IEnumerable<SearchResult> results) => results
        .Select((result, index) => (
            Item: SearchResultHelper.CreateUiResult(result, string.Empty, index, isApplication: false, scope: null),
            Modified: ReadModified(result)))
        .ToList();

    private static QuickPanelGroupViewModel Create(
        QuickPanelFolderSource source,
        QuickPanelGroupPreference? preference,
        List<(AppSearchResult Item, DateTime? Modified)> items,
        bool isLoading) => new(
            source.Id,
            TitleOf(source, preference),
            source.Path,
            items,
            QuickPanelGroupPreference.DefaultSortFor(source),
            preference?.ThumbnailView ?? true,
            preference?.Expanded ?? true,
            source.AcceptsDrops,
            isLoading: isLoading,
            maxItems: source.MaxItems);

    private static string TitleOf(QuickPanelFolderSource source, QuickPanelGroupPreference? preference)
        => string.IsNullOrWhiteSpace(preference?.DisplayName)
            ? QuickPanelFolderSource.DefaultName(source.Path)
            : preference!.DisplayName.Trim();

    private static DateTime? ReadModified(SearchResult item)
        => item.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : null;
}
