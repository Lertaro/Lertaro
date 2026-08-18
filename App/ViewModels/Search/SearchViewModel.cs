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

    private string _advancedQuery = string.Empty;
    private List<AppSearchResult> _allResults = new();
    private string _resultCountText = "";
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    private SearchSidebarCountHelper? _sidebarCountHelper;
    // Deliberately does not toggle IsResultsListEnabled while searching -- that used to disable the
    // list mid-search, which caused a Win32 disabled-theme flash and blocked immediate navigation.
    public bool IsSearching
    {
        get => _isSearching;
        private set => SetProperty(ref _isSearching, value);
    }
    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        private set => SetProperty(ref _isResultsListEnabled, value);
    }
    public SearchViewModel(string initialQuery = "")
    {
        _searchService = new SearchService();
        _searchEngine = new SearchExecutionEngine(_searchService);
        FilteredResults = new ObservableRangeCollection<AppSearchResult>();

        _serviceStatus = new SearchServiceStatusViewModel(this, _searchService);
        _serviceStatus.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        _dispatcher = new SearchQueryDispatchController(
            _searchEngine,
            _serviceStatus,
            getAllResults: () => _allResults,
            setAllResults: v => _allResults = v,
            setIsSearching: v => IsSearching = v,
            setLoadingPanelVisibility: v => LoadingPanelVisibility = v,
            setIsSearchBoxEnabled: v => IsSearchBoxEnabled = v,
            setReceivedCount: count =>
            {
                if (DynamicSidebarGroups.All(group => group.CombinedPredicate == null))
                    ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], count);
            },
            updateSidebarCounts: (batch, final) => _sidebarCountHelper?.Update(batch, final),
            applyFiltersAndRender: ApplyFiltersAndRender);

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
        _sidebarCountHelper = new SearchSidebarCountHelper(DynamicSidebarGroups.SelectMany(group => group.Items).ToList());

        // Seeds the results grid's sort state from whatever was last clicked THIS app run (see
        // SearchResultSortMemory's own comment) so reopening the full window keeps showing the same
        // sort instead of resetting to unsorted every time a fresh SearchViewModel is constructed.
        _currentSortColumn = SearchResultSortMemory.CurrentSortColumn;
        _isSortAscending = SearchResultSortMemory.IsSortAscending;

        ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], 0);
        AdvancedQuery = initialQuery;

        TranslationManager.Instance.PropertyChanged += OnTranslationsChanged;
    }
    private void OnTranslationsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
        {
            OnPropertyChanged(nameof(WindowTitle));
            // "N results" was formatted once with the old language's template string and never
            // recomputed until the next search/filter/sort -- refresh it here too so it isn't stuck
            // showing a stale language until the user happens to trigger one of those.
            ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], FilteredResults.Count);
            DynamicSidebarTranslationHelper.Refresh(DynamicSidebarGroups);
        }
    }

    // Properties
    // ==========================================

    public ObservableRangeCollection<AppSearchResult> FilteredResults { get; }
    public ObservableCollection<DynamicSidebarGroupViewModel> DynamicSidebarGroups { get; } = new();
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

    // ==========================================
    // Service status properties delegation
    // ==========================================

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

    public void OnDynamicFilterChanged() => ApplyFiltersAndRender(extendsContent: false, unchangedPrefix: 0);
    private readonly DynamicFilterCoordinator _dynamicFilterCoordinator = new();

    // DynamicFilterCoordinator renders through an Action<List<AppSearchResult>> and can do so twice
    // (immediately with the unfiltered list, then again once async predicates resolve), so the flag
    // rides on the instance rather than through that callback's signature.
    private bool _renderExtendsContent;
    private int _renderUnchangedPrefix;

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

        // Query-token providers (sort/filter/etc) have already been applied to _allResults by the
        // time this runs -- this only handles the column-header sort and dynamic sidebar filters.
        _dynamicFilterCoordinator.Apply(
            _allResults,
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
            () => _allResults,
            RenderFinal,
            v => IsSearching = v);
    }

    private void RenderFinal(List<AppSearchResult> finalResults)
    {
        // ReplaceRange's single Reset notification makes WPF discard and regenerate every LstGridResults
        // container from the top on every keystroke -- Quick/Inline hit this same cost long ago and fixed
        // it via SearchResultsReconciler.Replace (row-by-row Replace/Add/Remove, recycling containers
        // instead of tearing them down); this had never been ported to the full window's own render path.
        // No selection-preserving currentSelection/setSelection pair like that reconciler uses: this
        // window has no VM-level "selected result" property to preserve in the first place (ResultsControl
        // .xaml.cs's own shared OnCollectionChanged already resets ActiveListBox.SelectedIndex on every
        // change here, same as it always has).
        // The unchanged-prefix promise only holds if nothing reordered or removed rows on the way here.
        // A column sort or a sidebar filter produces a different list object than the one the
        // accumulator built the promise about, which is exactly the signal that it no longer applies.
        var unchangedPrefix = ReferenceEquals(finalResults, _allResults) ? _renderUnchangedPrefix : 0;
        FilteredResults.ReconcileTo(finalResults, SearchResultsReconciler.ItemsEqual, _renderExtendsContent, unchangedPrefix);
        ResultCountText = string.Format(TranslationManager.Instance["Search_Total"], finalResults.Count);
        OnPropertyChanged(nameof(ShowNoResultsHint));
        OnPropertyChanged(nameof(ShowWelcomeHint));
    }

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

    public bool ShowNoResultsHint => !IsActionsMode && FilteredResults.Count == 0 && !string.IsNullOrWhiteSpace(AdvancedQuery);
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
