using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.ViewModels.Service;

using Lertaro.Core.Services.Search;

using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search.DynamicSidebar;
namespace Lertaro.App.ViewModels.Search;

public class SearchViewModel : ViewModelBase, IDisposable
{
    // The full window returns everything that matches rather than a ranked first page. Core bounds this
    // by the index itself (see NameSearch), so the value only has to be larger than any drive's row
    // count. What it costs is linear in the number of matches -- measured at roughly 1.5us and 2KB per
    // result -- so a one-character query over a multi-million-row drive is seconds and gigabytes, not
    // milliseconds. That is the trade this constant makes.
    internal const int FullSearchFileLimit = int.MaxValue;
    internal const int FullSearchAppLimit = 0;

    // What the quick window borrows on its token path. It used to borrow FullSearchFileLimit, which was
    // 1000 -- now that the full window is unbounded, borrowing it would make an ordinary keystroke in the
    // quick window pay for every match on the drive, which is the opposite of what that path is for.
    internal const int TokenQuickSearchFileLimit = 1000;

    private readonly SearchService _searchService;
    private readonly SearchExecutionEngine _searchEngine;
    private readonly SearchServiceStatusViewModel _serviceStatus;
    private readonly SearchQueryDispatchController _dispatcher;
    private readonly SearchViewResultRenderer _resultRenderer;

    private string _advancedQuery = string.Empty;
    private List<AppSearchResult> _allResults = new();
    // Whether _allResults holds content-provider rows (ContentSearch's "cs " hits), reported by the code
    // that puts them in. Needed because a TYPE filter has to drop them, and finding out by asking the list
    // costs a full scan of it -- see _filterSource.
    private bool _allResultsHoldContentRows;
    private string _resultCountText = "";
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    private SearchSidebarCountHelper? _sidebarCountHelper;
    // Deliberately does not toggle IsResultsListEnabled while searching -- that used to disable the
    // list mid-search, which caused a Win32 disabled-theme flash and blocked immediate navigation.
    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                // The hint below reads this, and nothing else re-raises it: without this an empty list
                // during a search still paints "no results", which is what makes a window that is
                // working look like a window that found nothing.
                OnPropertyChanged(nameof(ShowNoResultsHint));
            }
        }
    }
    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        private set => SetProperty(ref _isResultsListEnabled, value);
    }
    public SearchViewModel(string initialQuery = "", IReadOnlyList<AppSearchResult>? quickSearchRows = null)
    {
        _searchService = new SearchService();
        _searchEngine = new SearchExecutionEngine(_searchService);
        FilteredResults = new ObservableRangeCollection<AppSearchResult>();

        _serviceStatus = new SearchServiceStatusViewModel(this, _searchService);
        _serviceStatus.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);
        _resultRenderer = new SearchViewResultRenderer(
            FilteredResults,
            () => _renderExtendsContent,
            finalResults => ReferenceEquals(finalResults, _allResults) ? _renderUnchangedPrefix : 0,
            count => ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], count),
            () =>
            {
                OnPropertyChanged(nameof(ShowNoResultsHint));
                OnPropertyChanged(nameof(ShowWelcomeHint));
            });

        _dispatcher = new SearchQueryDispatchController(
            _searchEngine,
            _serviceStatus,
            getAllResults: () => _allResults,
            setAllResults: (v, holdsContentRows) =>
            {
                _allResults = v;
                _allResultsHoldContentRows = holdsContentRows;
            },
            setIsSearching: v => IsSearching = v,
            setLoadingPanelVisibility: v => LoadingPanelVisibility = v,
            setIsSearchBoxEnabled: v => IsSearchBoxEnabled = v,
            setReceivedCount: count =>
            {
                if (DynamicSidebarGroups.All(group => group.CombinedPredicate == null))
                    ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], count);
            },
            updateSidebarCounts: (batch, final) => _sidebarCountHelper?.Update(batch, final),
            replaceSidebarCounts: results => _sidebarCountHelper?.Replace(results),
            applyFiltersAndRender: ApplyFiltersAndRender,
            isTypeFilterSelected: () => IsTypeFilterSelected);

        // Initialize dynamic plugin sidebar groups -- PluginManager.SidebarFilterProviders already
        // applies the user's saved order (falling back to each provider's own SortOrder).
        var orderedProviders = PluginManager.Instance.SidebarFilterProviders.ToList();

        foreach (var provider in orderedProviders)
        {
            foreach (var group in provider.GetFilterGroups())
            {
                DynamicSidebarGroups.Add(new DynamicSidebarGroupViewModel(group, this));
            }
        }

        if (DynamicSidebarGroups.Count > 0)
        {
            DynamicSidebarGroups[0].IsFirst = true;
        }
        _sidebarCountHelper = new SearchSidebarCountHelper(DynamicSidebarGroups);

        // Seeds the results grid's sort state from whatever was last clicked THIS app run (see
        // SearchResultSortMemory's own comment) so reopening the full window keeps showing the same
        // sort instead of resetting to unsorted every time a fresh SearchViewModel is constructed.
        _currentSortColumn = SearchResultSortMemory.CurrentSortColumn;
        _isSortAscending = SearchResultSortMemory.IsSortAscending;

        ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], 0);
        if (quickSearchRows is { Count: > 0 })
            HandOffQuickSearchResults(quickSearchRows);
        AdvancedQuery = initialQuery;

        TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
    }

    /// <summary>
    /// Shows what the quick window had on screen in this window immediately, underneath the wider search
    /// that the same query re-issues from here.
    /// </summary>
    /// <remarks>
    /// The full window asks the index for every match on the drive, so on a big index its first paint is
    /// seconds away; showing the rows the user was already looking at costs one list copy and turns the
    /// switch from "the results disappeared" into "the same results, in a bigger window". The first paint
    /// of the real search replaces them, which is why they need no invalidation of their own.
    /// </remarks>
    internal void HandOffQuickSearchResults(IReadOnlyList<AppSearchResult> rows)
    {
        if (rows.Count == 0)
            return;

        _allResults = new List<AppSearchResult>(rows);
        ApplyFiltersAndRender(extendsContent: false, unchangedPrefix: 0);
        OnPropertyChanged(nameof(ShowNoResultsHint));
    }
    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
        {
            OnPropertyChanged(nameof(WindowTitle));
            // Refresh the formatted count too; it was created with the previous language's template.
            ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], FilteredResults.Count);
            DynamicSidebarTranslationHelper.Refresh(DynamicSidebarGroups);
        }
    }

    public ObservableRangeCollection<AppSearchResult> FilteredResults { get; }
    public ObservableCollection<DynamicSidebarGroupViewModel> DynamicSidebarGroups { get; } = new();

    // True when any item of the well-known result-type group is selected. Content-search results
    // are only merged into _allResults while no type filter is active; a selected type (e.g. "文件")
    // means the user asked for exactly that type, so the extra content rows are excluded.
    internal bool IsTypeFilterSelected =>
        DynamicSidebarGroups.Any(g => string.Equals(g.Id, "Type", StringComparison.OrdinalIgnoreCase) && g.HasSelection);
    public string AdvancedQuery
    {
        get => _advancedQuery;
        set
        {
            if (SetProperty(ref _advancedQuery, value))
            {
                _sidebarCountHelper?.Reset();
                if (string.IsNullOrWhiteSpace(value))
                {
                    _searchEngine.CancelPendingSearch();
                    _dispatcher.PerformSearch(value);
                }
                else
                {
                    _searchEngine.CancelPendingSearch();
                    _dispatcher.OnAdvancedQueryChanged(value);
                }
                OnPropertyChanged(nameof(ShowWelcomeHint));
                OnPropertyChanged(nameof(ShowNoResultsHint));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    // "<keyword> - <app title>" while there's a query, falling back to the plain translated title once
    // it's cleared -- lets the taskbar/Alt+Tab entry identify which search this window is showing.
    // Re-raised on AdvancedQuery changes above and on translation reload below (OnPropertyChanged("Item[]")
    // is TranslationManager's own convention for "every indexer-bound string may have changed").
    public string WindowTitle => string.IsNullOrWhiteSpace(AdvancedQuery)
        ? TranslationManager.Instance["Search_Title"]
        : $"{AdvancedQuery} - {TranslationManager.Instance["Search_Title"]}";

    public string ResultCountText
    {
        get => _resultCountText;
        private set => SetProperty(ref _resultCountText, value);
    }

    public bool IsSearchBoxEnabled
    {
        get => _serviceStatus.IsSearchBoxEnabled;
        set => _serviceStatus.IsSearchBoxEnabled = value;
    }

    public bool IsServiceConnected => _serviceStatus.IsServiceConnected;

    public Visibility LoadingPanelVisibility
    {
        get => _serviceStatus.LoadingPanelVisibility;
        internal set => _serviceStatus.LoadingPanelVisibility = value;
    }

    public Visibility ProgressBarVisibility => _serviceStatus.ProgressBarVisibility;
    public bool IsProgressIndeterminate => _serviceStatus.IsProgressIndeterminate;
    public double LoadingProgress
    {
        get => _serviceStatus.LoadingProgress;
        set => _serviceStatus.LoadingProgress = value;
    }
    public Visibility ErrorIconVisibility => _serviceStatus.ErrorIconVisibility;
    public string LoadingTitle => _serviceStatus.LoadingTitle;
    public string LoadingStats => _serviceStatus.LoadingStats;
    public Visibility InstallButtonVisibility => _serviceStatus.InstallButtonVisibility;
    public ICommand InstallServiceCommand => _serviceStatus.InstallServiceCommand;

    private string _currentSortColumn = string.Empty;
    private bool _isSortAscending = true;

    public bool IsSortAscending => _isSortAscending;
    public string CurrentSortColumn => _currentSortColumn;

    public void SortByColumn(string columnId)
    {
        (_currentSortColumn, _isSortAscending) = SearchResultSortCycle.Advance(_currentSortColumn, _isSortAscending, columnId);
        SearchResultSortMemory.CurrentSortColumn = _currentSortColumn;
        SearchResultSortMemory.IsSortAscending = _isSortAscending;
        // Re-sorting by a column reorders everything under the user, so whatever row they were looking
        // at is no longer where -- or what -- it was. Same for a sidebar filter. Both are a new result
        // set as far as the list's scroll position is concerned, however unchanged the query is.
        ApplyFiltersAndRender(extendsContent: false, unchangedPrefix: 0);
    }

    public void OnDynamicFilterChanged() { _sidebarCountHelper?.Recalculate(); ApplyFiltersAndRender(extendsContent: false, unchangedPrefix: 0); }
    private readonly DynamicFilterCoordinator _dynamicFilterCoordinator = new();

    // DynamicFilterCoordinator renders through an Action<List<AppSearchResult>> and can do so twice
    // (immediately with the unfiltered list, then again once async predicates resolve), so the flag
    // rides on the instance rather than through that callback's signature.
    private bool _renderExtendsContent;
    private int _renderUnchangedPrefix;
    // The list currently being filtered/rendered. Normally _allResults itself; when a TYPE filter is
    // selected it is a filtered copy without the full-search-file-provider rows (e.g. ContentSearch
    // hits), so selecting "文件" excludes those rows even if an earlier unfiltered render merged them.
    private List<AppSearchResult>? _filterSource;

    private void ApplyFiltersAndRender(bool extendsContent, int unchangedPrefix)
    {
        if (_allResults == null) return;
        _renderExtendsContent = extendsContent;
        _renderUnchangedPrefix = unchangedPrefix;

        var activeFilters = DynamicSidebarGroups
            .Select(g => g.CombinedPredicate)
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        // The copy costs a full pass over a list that can hold hundreds of thousands of rows, so it happens
        // only when a type filter is open AND this list actually holds content rows. Those arrive once per
        // query, on the append that adds them -- every streaming paint before that was paying the copy to
        // filter a list that had nothing in it to filter.
        _filterSource = IsTypeFilterSelected && _allResultsHoldContentRows
            ? _allResults.Where(r => !r.IsFullSearchFileResult).ToList()
            : _allResults;

        // Query-token providers (sort/filter/etc) have already been applied to _allResults by the
        // time this runs -- this only handles the column-header sort and dynamic sidebar filters.
        _dynamicFilterCoordinator.Apply(
            _filterSource,
            activeFilters,
            // ToList only when the sort actually reordered something. With no column selected --
            // relevance order, the default -- Sort hands back the very list it was given, and
            // materializing that again is another full-size copy per paint for no change at all.
            results =>
            {
                var sorted = SearchResultSorter.Sort(results, _currentSortColumn, _isSortAscending);
                return ReferenceEquals(sorted, results) && results is List<AppSearchResult> asList
                    ? asList
                    : sorted.ToList();
            },
            () => _filterSource!,
            RenderFinal,
            v => IsSearching = v);
    }

    private void RenderFinal(List<AppSearchResult> finalResults) => _resultRenderer.Render(finalResults);

    private bool _isActionsMode;
    public bool IsActionsMode
    {
        get => _isActionsMode;
        set
        {
            if (SetProperty(ref _isActionsMode, value))
            {
                OnPropertyChanged(nameof(ShowNoResultsHint));
                OnPropertyChanged(nameof(ShowWelcomeHint));
            }
        }
    }

    // False while a search is still running: an empty list then means "nothing has arrived yet", not
    // "there is nothing to find", and the window reads as blank either way.
    public bool ShowNoResultsHint => !IsActionsMode && !IsSearching && FilteredResults.Count == 0 && !string.IsNullOrWhiteSpace(AdvancedQuery);
    public bool ShowWelcomeHint => !IsActionsMode && string.IsNullOrWhiteSpace(AdvancedQuery);

    internal void PerformSearch(string query) => _dispatcher.PerformSearch(query);

    public void Dispose()
    {
        TranslationManager.Instance.PropertyChanged -= OnTranslationsChanged;
        _searchEngine.Dispose();
        _serviceStatus.Dispose();
        _searchService.Dispose();
    }
}
