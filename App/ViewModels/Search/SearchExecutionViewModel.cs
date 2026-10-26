using System.Windows;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.Core.Services.Search;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

public class SearchExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly QuickSearchViewModel _mainVm;
    private readonly SearchExecutionEngine _engine;
    private readonly SearchDispatchController _dispatcher;

    private string _searchQuery = null!;
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    private AppSearchResult? _selectedResult;

    // UI Panel Visibilities
    private Visibility _resultsPanelVisibility = Visibility.Collapsed;
    private Visibility _resultsSeparatorVisibility = Visibility.Collapsed;
    private string? _searchScope;
    private bool _isInlineSearchContext;
    private readonly System.Windows.Threading.DispatcherTimer _providerLoadedRefreshTimer;

    public SearchExecutionViewModel(QuickSearchViewModel mainVm, SearchService searchService)
    {
        _mainVm = mainVm;
        _engine = new SearchExecutionEngine(
            searchService,
            // The enum values are the wire values, so the cast is the whole mapping.
            () => (Lertaro.Core.Wire.SearchCategoryFilter)(byte)(_mainVm?.SelectedCategory ?? SearchResultCategory.All));
        Results = new ObservableRangeCollection<AppSearchResult>();

        _dispatcher = new SearchDispatchController(
            _engine,
            _mainVm,
            getSearchScope: () => SearchScope,
            getIsInlineSearchContext: () => IsInlineSearchContext,
            getSearchQuery: () => SearchQuery,
            setIsSearching: v => IsSearching = v,
            setResultsPanelVisibility: v => ResultsPanelVisibility = v,
            setResultsSeparatorVisibility: v => ResultsSeparatorVisibility = v,
            replaceResults: ReplaceResults,
            getResultsCount: () => Results.Count);

        // Coalesce multiple providers finishing their (background, unawaited) load in quick succession
        // (e.g. right after app startup) into a single re-run of the current query, not one per provider.
        _providerLoadedRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _providerLoadedRefreshTimer.Tick += (s, e) =>
        {
            _providerLoadedRefreshTimer.Stop();
            if (!IsActionsMode && !string.IsNullOrWhiteSpace(_searchQuery))
                _dispatcher.DispatchSearch(_searchQuery);
        };
        SearchableItemMapper.ProviderLoaded += OnSearchableItemProviderLoaded;
    }

    // Raised from a background thread (see SearchableItemMapper.ProviderLoaded) whenever a searchable-item
    // provider finishes loading. A query issued before that point silently missed that provider's items
    // (AddSearchableItemResults skips providers that aren't cached yet), so re-run the current query to let
    // those items stream in -- ReplaceResults reconciles in place, so this doesn't reset/flicker the list.
    private void OnSearchableItemProviderLoaded() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
    {
        _providerLoadedRefreshTimer.Stop();
        _providerLoadedRefreshTimer.Start();
    }));

    public ObservableRangeCollection<AppSearchResult> Results { get; }

    public AppSearchResult? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public string? SearchScope
    {
        get => _searchScope;
        set => SetProperty(ref _searchScope, value);
    }

    public bool IsInlineSearchContext
    {
        get => _isInlineSearchContext;
        set => SetProperty(ref _isInlineSearchContext, value);
    }

    private bool _isActionsMode;
    public bool IsActionsMode
    {
        get => _isActionsMode;
        set => SetProperty(ref _isActionsMode, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                // Every typed change is recorded here, not only the ones that reach PerformSearch: a non-empty
                // query goes through DispatchSearch instead, so saving only in PerformSearch left this empty
                // and a filter change had nothing to re-run.
                _currentQuery = value ?? string.Empty;

                if (IsActionsMode)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    _engine.CancelPendingSearch();
                    _dispatcher.PerformSearch(value ?? string.Empty);
                }
                else
                {
                    _dispatcher.DispatchSearch(value);
                }
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
            {
                // Keep list enabled during search to prevent Win32 system disabled theme flash and allow immediate navigation
                // IsResultsListEnabled = !value;
            }
        }
    }

    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        set => SetProperty(ref _isResultsListEnabled, value);
    }

    public Visibility ResultsPanelVisibility
    {
        get => _resultsPanelVisibility;
        set => SetProperty(ref _resultsPanelVisibility, value);
    }

    public Visibility ResultsSeparatorVisibility
    {
        get => _resultsSeparatorVisibility;
        set => SetProperty(ref _resultsSeparatorVisibility, value);
    }

    // SearchQuery's setter only re-runs PerformSearch when the value changes, so re-showing the window
    // while the box stays empty wouldn't otherwise notice anything that changed in the meantime.
    public void RefreshEmptyState()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
            _dispatcher.PerformSearch(_searchQuery);
    }

    // Set when the session's FIRST search starts, cleared when its results arrive: the mapper is warmed at
    // startup, so whatever is left of the first search's wait is on the service side (pipe + index) or in the
    // UI, and only an end-to-end number can tell those apart. Information only; changes no behaviour.
    private long _firstSearchStarted;

    // The query currently being shown, kept so re-running after a filter change can reuse it without asking
    // the window for its box text (which may have moved on by then).
    private string _currentQuery = string.Empty;

    public void PerformSearch(string query)
    {
        if (_firstSearchStarted == 0)
            _firstSearchStarted = Environment.TickCount64;

        _currentQuery = query ?? string.Empty;
        _dispatcher.PerformSearch(query ?? string.Empty);
    }

    private void ReplaceResults(IEnumerable<AppSearchResult> results)
    {
        if (_firstSearchStarted != 0)
        {
            Lertaro.Core.Logger.Log(
                $"[SearchTiming] First search of the session produced results in {Environment.TickCount64 - _firstSearchStarted} ms.",
                Lertaro.Core.LogLevel.Info);
            _firstSearchStarted = 0;
        }

        // The unfiltered list is kept so switching filter does not need another search: the rows are already
        // here, only which of them are shown changes.
        _unfilteredResults = results as IReadOnlyList<AppSearchResult> ?? results.ToList();
        ApplyCategoryFilter();
    }

    /// <summary>
    /// The full result list as last produced by a search, before the category filter. Held so a filter change
    /// is a re-projection, never a re-query.
    /// </summary>
    private IReadOnlyList<AppSearchResult> _unfilteredResults = [];

    private SearchResultCategory _selectedCategory = SearchResultCategory.All;

    /// <summary>
    /// Which filter the list shows. Changing it re-projects the rows that are already loaded; the selection
    /// moves to the first row that survives, so the keyboard flow keeps working across a switch.
    /// </summary>
    public SearchResultCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value)
                return;

            _selectedCategory = value;
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged(nameof(CategorySummary));

            // A filter change re-runs the query instead of re-projecting the rows already held: the rows were
            // produced under the previous filter, and under the whole-result ordering a short query fills the
            // cap with applications and folders long before any audio ranks. Re-querying lets the filter run
            // inside the candidate build, where it still has the whole result set to choose from.
            if (!string.IsNullOrEmpty(_currentQuery))
                PerformSearch(_currentQuery);
        }
    }

    // "12 个结果 · 图片" style summary for the status bar; All reads as no suffix at all.
    public string CategorySummary =>
        _selectedCategory == SearchResultCategory.All
            ? string.Empty
            : TranslationManager.Instance[CategoryTranslationKey(_selectedCategory)];

    internal static string CategoryTranslationKey(SearchResultCategory category) => category switch
    {
        SearchResultCategory.Folder => "Search_Category_Folder",
        SearchResultCategory.Application => "Search_Category_Application",
        SearchResultCategory.Settings => "Search_Category_Settings",
        SearchResultCategory.Document => "Search_Category_Document",
        SearchResultCategory.Image => "Search_Category_Image",
        SearchResultCategory.Audio => "Search_Category_Audio",
        SearchResultCategory.Video => "Search_Category_Video",
        _ => "Search_Category_All",
    };

    private void ApplyCategoryFilter()
    {
        // No re-filtering here. The engine already restricted the answer to this category (see
        // SearchCategoryFilter), so these rows are the category's rows. Filtering again with the caller-side
        // classifier would drop anything the two disagree about -- and they do disagree, because the engine can
        // only read a path while this side can also read who produced the row. A second pass can only lose
        // rows, never add them.
        var rows = _unfilteredResults;



        SearchResultsReconciler.Replace(Results, rows, SelectedResult, v => SelectedResult = v);
    }

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    // The inline window pre-warms its folder listing the moment its scope is known, i.e. before the user
    // has typed -- see DirectChildrenListingCache for why that is the difference between a first character
    // that matches instantly and one that waits for a whole folder walk.
    public void PrewarmDirectoryListing(string? directory) => _engine.PrewarmDirectoryListing(directory);

    public void InvalidateDirectoryListing(string? directory) => _engine.InvalidateDirectoryListing(directory);

    public void Dispose()
    {
        SearchableItemMapper.ProviderLoaded -= OnSearchableItemProviderLoaded;
        _providerLoadedRefreshTimer.Stop();
        _engine.Dispose();
    }
}
