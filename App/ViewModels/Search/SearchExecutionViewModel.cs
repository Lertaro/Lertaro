using System.Windows;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.Core.Services.Search;

using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

public class SearchExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly QuickSearchViewModel _mainVm;
    private readonly SearchExecutionEngine _engine;
    private readonly SearchDispatchController _dispatcher;

    private string _searchQuery = null!;
    private string _lastQuery = string.Empty;
    private IReadOnlyList<SidebarFilterItem> _categories = BuildCategoryTabs();
    private SidebarFilterItem _selectedCategory = null!;
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
        // The list's own first row, not a second 全部 of the same shape: the bar selects by reference, so a
        // look-alike would leave the row with nothing selected and no underline on the first paint.
        _selectedCategory = _categories[0];
        _engine = new SearchExecutionEngine(searchService, () => _selectedCategory.Category ?? SearchCategoryFilter.All);
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
            // Normalised once, here: the property is non-nullable but a binding can hand a null over,
            // and the calls below take a non-nullable string. Storing the normalised value keeps a null
            // from reaching the field at all (every reader already treats empty as "no query").
            var query = value ?? string.Empty;
            if (SetProperty(ref _searchQuery, query))
            {
                _lastQuery = query;
                // The type bar appears with the first keystroke and leaves with the last one; both it and the
                // Tab cycle read the typed-text condition, so they have to be re-announced here. Announced
                // before the actions-mode return: leaving actions mode must not keep a stale hidden bar.
                OnPropertyChanged(nameof(CategoryBarVisibility));
                if (IsActionsMode)
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    _engine.CancelPendingSearch();
                    _dispatcher.PerformSearch(query);
                }
                else
                {
                    _dispatcher.DispatchSearch(query);
                }
            }
        }
    }

    /// <summary>
    /// The filter tabs: 全部 first, then the type rows the Core Extensions plugin's 类型设置 describes. This
    /// bar has no list of its own -- it reads the same <see cref="SidebarFilterItem"/> rows the full search
    /// window's sidebar builds, through the same provider list and the same per-row predicates -- so turning
    /// a row off, renaming a :@keyword or adding one changes this bar in the same step, and the two surfaces
    /// cannot answer differently for one row.
    /// </summary>
    public IReadOnlyList<SidebarFilterItem> Categories => _categories;

    /// <summary>
    /// The active tab. A change has to re-run the query: the engine narrows the index walk to the tab's
    /// category where it can, so there is no unfiltered list kept here that could be filtered differently.
    /// </summary>
    public SidebarFilterItem SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory == value)
                return;
            _selectedCategory = value;
            OnPropertyChanged(nameof(SelectedCategory));
            // PerformSearch, not DispatchSearch: switching a tab is one deliberate action, not a keystroke in
            // a stream of them, so it has nothing to debounce against -- waiting 30 ms to coalesce it with a
            // later keystroke only delays the only thing the user is waiting for. Typing keeps the debounce.
            if (!IsActionsMode && !string.IsNullOrWhiteSpace(_lastQuery))
                _dispatcher.PerformSearch(_lastQuery);
        }
    }

    /// <summary>
    /// The active tab's predicate, or null when the tab is 全部. Null is what the rest of the pipeline reads
    /// as "no narrowing": the display cap, and the show-more row it feeds, apply to an unfiltered list and
    /// must not apply to a filtered one -- a tab that keeps every row is not the same thing as no tab, and
    /// handing <c>_ =&gt; true</c> downstream would silently disable the cap.
    /// </summary>
    public Func<ISearchResult, bool>? ActiveCategoryFilter =>
        ReferenceEquals(_selectedCategory, _categories[0]) ? null : _selectedCategory.MatchPredicate;

    /// <summary>Puts the bar back on 全部, the way a window that was only just opened reads.</summary>
    public void ResetCategoryFilter() => SelectedCategory = _categories[0];

    /// <summary>
    /// Re-reads the rows. Cheap, and it is the only way a settings edit while this window is open reaches the
    /// bar: providers are asked for their groups at construction, so the host asks again.
    /// </summary>
    public void RebuildCategories()
    {
        var rebuilt = BuildCategoryTabs();

        // Same pass, same reason: the bar's switch is a setting a user can change while this window is open.
        ShowCategoryBar = Core.UserSettings.Load().SearchWindow.ShowCategoryFilter;

        // Identity is the row's id, not the object: a provider hands back new instances every call, and a
        // user mid-filter must not be bounced to 全部 because the list was refreshed under them.
        // A HIDDEN bar is the exception: keeping a filter it applied would leave the user no control to clear
        // it, so hiding the bar puts it back on 全部.
        var keep = ShowCategoryBar
            ? rebuilt.FirstOrDefault(row => row.Id == _selectedCategory.Id) ?? rebuilt[0]
            : rebuilt[0];
        _categories = rebuilt;
        OnPropertyChanged(nameof(Categories));
        if (keep != _selectedCategory)
        {
            _selectedCategory = keep;
            OnPropertyChanged(nameof(SelectedCategory));
        }
    }

    private static IReadOnlyList<SidebarFilterItem> BuildCategoryTabs()
    {
        var tabs = new List<SidebarFilterItem> { AllTab() };
        foreach (var group in PluginManager.Instance.SidebarFilterProviders.SelectMany(provider => provider.GetFilterGroups()))
        {
            // Only the type group: the same provider list also carries file size and date rows, which are
            // ranges rather than kinds and would make a bar of things that are not "what kind of entry".
            if (group.Id == "Type")
                tabs.AddRange(group.Items);
        }

        return tabs;
    }

    // The unfiltered tab. No category, so the engine walks everything; a predicate that keeps everything, so
    // the display cap applies to it as it always did.
    private static SidebarFilterItem AllTab() => new()
    {
        Id = string.Empty,
        DisplayName = TranslationManager.Instance["Search_Category_All"],
        MatchPredicate = _ => true,
    };

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
        set
        {
            if (SetProperty(ref _resultsPanelVisibility, value))
                OnPropertyChanged(nameof(CategoryBarVisibility));
        }
    }

    // Whether the type filter bar is shown at all -- Settings → Quick Search Window. Re-read with the rows
    // (see RebuildCategories), because that is the pass an edit made while this window is open reaches.
    private bool _showCategoryBar = true;

    public bool ShowCategoryBar
    {
        get => _showCategoryBar;
        private set
        {
            if (SetProperty(ref _showCategoryBar, value))
                OnPropertyChanged(nameof(CategoryBarVisibility));
        }
    }

    /// <summary>
    /// The bar's own visibility: while the user wants the bar and has typed something. The results panel's
    /// visibility is deliberately NOT part of this -- a query that matched nothing must still show the tabs,
    /// because switching tab is how the user goes looking for what the current one did not find.
    /// </summary>
    /// <remarks>
    /// The typed-text condition is what keeps the bar out of the empty state. Summoning the window shows
    /// recent and favourite rows, so the results panel is already visible before the user has typed
    /// anything; keying the bar off the panel alone put a row of type tabs above a list no query had
    /// produced.
    /// </remarks>
    public Visibility CategoryBarVisibility
        => _showCategoryBar && HasSearchText ? Visibility.Visible : Visibility.Collapsed;

    // Whether the user has typed a query. Trailing spaces do not count: the dispatcher would treat the box
    // as empty too, so the bar must not appear for them either.
    public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchQuery);

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
