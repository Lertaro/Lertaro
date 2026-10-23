using System.Windows;
using System.Windows.Interop;
using Lertaro.PluginSdk.Abstractions.Plugins;

using Lertaro.App.Services.AppWindow;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

// Owns dispatching the currently-selected actions-list row to whichever handler owns it (a plugin's
// direct delegate, a registered PluginManager action, a submenu, or a dynamic action provider) --
// split out of ShellMenuPresenter purely to keep that file under the file-length limit.
internal sealed class ActionsMenuExecutor
{
    private readonly ISearchWindow _view;
    private readonly Dictionary<uint, IDynamicActionProvider> _commandToProviderMap;
    private readonly ActionsMenuNavigator _navigator;
    private readonly Action _exitActionsMode;

    public ActionsMenuExecutor(
        ISearchWindow view,
        Dictionary<uint, IDynamicActionProvider> commandToProviderMap,
        ActionsMenuNavigator navigator,
        Action exitActionsMode)
    {
        _view = view;
        _commandToProviderMap = commandToProviderMap;
        _navigator = navigator;
        _exitActionsMode = exitActionsMode;
    }

    public void Execute(AppSearchResult? activeResult, IReadOnlyList<AppSearchResult> activeResults)
    {
        if (_view.LstActions.SelectedItem is not ActionMenuItem item)
            return;
        if (item.IsSeparator || item.IsSectionHeader || item.IsDisabled)
            return;

        // 0. Direct delegate (e.g. CustomActions dynamic provider)
        if (item.OnExecute != null)
        {
            RecordQuickSearchKeyword();
            if (!item.KeepWindowVisible)
            {
                _view.HideWindow();
                item.OnExecute();
                return;
            }

            // The action owns a modal dialog and must run while the search window is still visible;
            // exiting actions mode afterwards restores the query and keyboard focus. Defer the exit
            // until the dialog's own window has finished closing: restoring the search box and query
            // while the dialog is still tearing down can leave the IME attached to the destroyed
            // dialog, which manifests as a frozen input method until another window is focused.
            //
            // The modal dialog deactivates the quick window, and its normal focus-loss path would hide
            // the window behind the dialog. Flag the modal span so that path stays out of the way; a
            // hidden owner cannot be reactivated, which is what left the IME stranded before.
            var quick = _view as Lertaro.App.QuickSearchWindow;
            quick?.BeginModalAction();
            try
            {
                item.OnExecute();
            }
            finally
            {
                quick?.EndModalAction();
                _view.LstResults.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    () =>
                    {
                        _exitActionsMode();
                        RestoreKeyboardFocusAfterModal();
                    });
            }

            return;
        }

        // 1. Handle custom Lertaro actions dynamically from PluginManager

        var registration = PluginManager.Instance.GetActionByRuntimeId(item.CommandId);
        if (registration != null)
        {
            if (activeResult != null)
            {
                RecordQuickSearchKeyword();

                try
                {
                    // A plugin action can open a modal prompt owned by this window. Let it finish
                    // before hiding the owner, otherwise Win32 hides the prompt along with it.
                    PluginPerformanceMonitor.Measure(registration.Action, () => registration.Action.Execute(activeResults, _view));
                }
                finally
                {
                    if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                    {
                        _view.HideWindow();
                    }
                }
            }

            _exitActionsMode();
            return;
        }

        // 2. Handle submenus

        if (item.HasSubMenu)
        {
            _navigator.EnterSubMenu();
            return;
        }

        // 3. Handle dynamic action provider executions

        if (_commandToProviderMap.TryGetValue(item.CommandId, out var provider))
        {
            if (activeResult != null)
            {
                RecordQuickSearchKeyword();
                var hwnd = new WindowInteropHelper(_view as Window ?? System.Windows.Application.Current.MainWindow).Handle;
                PluginPerformanceMonitor.Measure(provider, () => provider.ExecuteCommand(activeResults, item.CommandId, hwnd));
                if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                {
                    _view.HideWindow();
                }
            }

            _exitActionsMode();
        }
    }

    private void RecordQuickSearchKeyword()
    {
        if (_view is QuickSearchWindow quickSearchWindow)
            quickSearchWindow.RecordKeywordHistory();
    }

    // A modal dialog disables and then destroys its owner-side keyboard focus. Activate the search
    // window again and give the search box keyboard focus on the next input pass so the IME reattaches
    // to a live HWND instead of the closed dialog.
    private void RestoreKeyboardFocusAfterModal()
    {
        if (Window.GetWindow(_view.LstResults) is not { } window)
            return;

        window.Activate();
        _view.SearchTextBox.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () =>
            {
                window.Activate();
                // Some IMEs stay attached to the closed file dialog until the input method is
                // re-associated with the search box; toggling it forces that re-association.
                System.Windows.Input.InputMethod.SetIsInputMethodEnabled(_view.SearchTextBox, false);
                System.Windows.Input.InputMethod.SetIsInputMethodEnabled(_view.SearchTextBox, true);
                _view.FocusSearch();
                System.Windows.Input.Keyboard.Focus(_view.SearchTextBox);
            });
    }
}
