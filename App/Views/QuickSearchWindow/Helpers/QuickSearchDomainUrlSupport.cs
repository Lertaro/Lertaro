using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using QuickSearchWindowView = Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Owns the quick-window-only domain completion so the general search and inline search behaviors remain unchanged.
internal sealed class QuickSearchDomainUrlSupport : IDisposable
{
    private static readonly TimeSpan QueryChangeDelay = TimeSpan.FromMilliseconds(80);
    private readonly QuickSearchWindowView _window;
    private readonly DispatcherTimer _queryTimer;
    private bool _isApplying;

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
        if (_isApplying)
            return;

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
        if (_isApplying || _window.IsInActionsMode || _window.ViewModel.IsSearching)
            return;

        if (_window.ViewModel.Results.Any(result => !result.IsEmptyResult && !result.IsSearchSectionHeader))
            return;

        if (!DomainUrlHelper.TryBuildHttpsUrl(_window.TxtSearch.Text, out var url))
            return;

        _isApplying = true;
        try
        {
            _window.TxtSearch.Text = url;
            _window.TxtSearch.CaretIndex = url.Length;
            _window.TxtSearch.SelectionStart = url.Length;
            _window.TxtSearch.SelectionLength = 0;
        }
        finally
        {
            _isApplying = false;
        }
    }
}
