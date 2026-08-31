using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using QuickSearchWindowView = Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Owns the quick-window-only domain suggestions so the general search and inline search behaviors remain unchanged.
internal sealed class QuickSearchDomainUrlSupport : IDisposable
{
    private static readonly TimeSpan QueryChangeDelay = TimeSpan.FromMilliseconds(80);
    private readonly QuickSearchWindowView _window;
    private readonly DispatcherTimer _queryTimer;
    public QuickSearchDomainUrlSupport(QuickSearchWindowView window)
    {
        _window = window;
        _queryTimer = new DispatcherTimer { Interval = QueryChangeDelay };
        _queryTimer.Tick += OnQueryTimerTick;
        _window.TxtSearch.TextChanged += OnSearchTextChanged;
        _window.ViewModel.Results.CollectionChanged += OnResultsChanged;
        _window.ViewModel.Search.PropertyChanged += OnSearchPropertyChanged;
    }

    public void Dispose()
    {
        _queryTimer.Stop();
        _queryTimer.Tick -= OnQueryTimerTick;
        _window.TxtSearch.TextChanged -= OnSearchTextChanged;
        _window.ViewModel.Results.CollectionChanged -= OnResultsChanged;
        _window.ViewModel.Search.PropertyChanged -= OnSearchPropertyChanged;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _queryTimer.Stop();
        _queryTimer.Start();
    }

    private void OnQueryTimerTick(object? sender, EventArgs e)
    {
        _queryTimer.Stop();
        TryCompleteDomainUrl();
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => TryCompleteDomainUrl();

    private void OnSearchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchExecutionViewModel.IsSearching) && !_window.ViewModel.IsSearching)
            TryCompleteDomainUrl();
    }

    private void TryCompleteDomainUrl()
    {
        if (_window.IsInActionsMode || _window.ViewModel.IsSearching)
            return;

        // Only replace the engine's explicit no-results row. An empty collection can be a transient
        // state while a new query is being dispatched, and must never be treated as a settled miss.
        if (!_window.ViewModel.Results.Any(result => result.IsEmptyResult))
            return;

        if (_window.ViewModel.Results.Any(result => !result.IsEmptyResult && !result.IsSearchSectionHeader))
            return;

        if (!DomainUrlHelper.TryBuildWebUrls(_window.TxtSearch.Text, out var httpsUrl, out var httpUrl))
            return;

        var query = _window.TxtSearch.Text;
        _window.ViewModel.Search.Results.ReplaceRange(new[]
        {
            CreateWebUrlResult(httpsUrl, query),
            CreateWebUrlResult(httpUrl, query)
        });
    }

    private static AppSearchResult CreateWebUrlResult(string url, string query) => new()
    {
        Name = url,
        FullPath = url,
        ParentDir = string.Empty,
        IsDir = false,
        Drive = string.Empty,
        ResultKind = "InstantResult",
        SearchQuery = query,
        IconOverride = FavoriteUrlHelper.Icon,
        InstantResultActionType = "Execute",
        InstantResultActionArgument = url
    };
}
