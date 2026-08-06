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
            _view.HideWindow();
            item.OnExecute();
            return;
        }

        // 1. Handle custom Lertaro actions dynamically from PluginManager

        var registration = PluginManager.Instance.GetActionByRuntimeId(item.CommandId);
        if (registration != null)
        {
            if (activeResult != null)
            {
                if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                {
                    _view.HideWindow();
                }

                registration.Action.Execute(activeResults, _view);
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
                var hwnd = new WindowInteropHelper(_view as Window ?? System.Windows.Application.Current.MainWindow).Handle;
                provider.ExecuteCommand(activeResults, item.CommandId, hwnd);
                if (!_view.GetType().Name.Equals("SearchWindow", StringComparison.Ordinal))
                {
                    _view.HideWindow();
                }
            }

            _exitActionsMode();
        }
    }
}
