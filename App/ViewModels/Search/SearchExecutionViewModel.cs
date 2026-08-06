using System.Windows;
using Lertaro.App.Helpers;
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
        _engine = new SearchExecutionEngine(searchService);
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
                if (IsActionsMode)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    _engine.CancelPendingSearch();
                    _dispatcher.PerformSearch(value);
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

    public void PerformSearch(string query) => _dispatcher.PerformSearch(query);

    private void ReplaceResults(IEnumerable<AppSearchResult> results) =>
        SearchResultsReconciler.Replace(Results, results, SelectedResult, v => SelectedResult = v);

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    public void Dispose()
    {
        SearchableItemMapper.ProviderLoaded -= OnSearchableItemProviderLoaded;
        _providerLoadedRefreshTimer.Stop();
        _engine.Dispose();
    }
}
