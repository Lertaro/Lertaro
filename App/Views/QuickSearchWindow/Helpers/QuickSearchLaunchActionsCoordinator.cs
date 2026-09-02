using System.Windows;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Keeps the temporary visibility and sizing changes needed when the quick launch panel hosts actions.
internal sealed class QuickSearchLaunchActionsCoordinator
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private bool _isActive;
    private bool _finishPending;
    private Visibility _resultsPanelVisibility;
    private Visibility _resultsListVisibility;
    private AppSearchResult? _selectedResult;

    internal QuickSearchLaunchActionsCoordinator(Lertaro.App.QuickSearchWindow window) => _window = window;

    internal void Enter(AppSearchResult result)
    {
        if (_window.MenuPresenter?.CanShowActionsMenu([result]) != true)
            return;

        if (!_isActive)
        {
            _isActive = true;
            _resultsPanelVisibility = _window.ViewModel.ResultsPanelVisibility;
            _resultsListVisibility = _window.GridSearchResults.Visibility;
            _selectedResult = _window.ViewModel.SelectedResult;
        }

        _window.ViewModel.SelectedResult = result;
        _window.ViewModel.ResultsPanelVisibility = Visibility.Visible;
        _window.GridSearchResults.Visibility = Visibility.Collapsed;
        _window.LaunchPanel.SetActionsModeHeight(expanded: true);
        _window.MenuPresenter.EnterActionsMode(result);
    }

    internal void PrepareExit()
    {
        if (!_isActive)
            return;

        _isActive = false;
        _finishPending = true;
        _window.LaunchPanel.SetActionsModeHeight(expanded: false);
        _window.ViewModel.SelectedResult = _selectedResult;
    }

    internal void FinishExit()
    {
        if (!_finishPending)
            return;

        _finishPending = false;
        _window.ViewModel.ResultsPanelVisibility = _resultsPanelVisibility;
        _window.GridSearchResults.Visibility = _resultsListVisibility;
        _window.SizeToContent = SizeToContent.Manual;
        _window.SizeToContent = SizeToContent.WidthAndHeight;
    }
}
