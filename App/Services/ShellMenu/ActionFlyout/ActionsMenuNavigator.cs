using System.Windows.Input;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Helpers;

namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

// Owns the shell-menu navigation stack (drilling into/out of submenus) for ShellMenuPresenter --
// split out purely to keep that file under the file-length limit.
internal sealed class ActionsMenuNavigator
{
    private readonly ISearchWindow _view;
    private readonly Func<IntPtr, Task> _loadMenuItems;
    private readonly Action _exitActionsMode;

    private readonly Stack<IntPtr> _menuStack = new();
    private readonly Stack<int> _menuSelectedIndexStack = new();
    private readonly Stack<string> _menuTitleStack = new();
    private int _navigationVersion;

    public ActionsMenuNavigator(ISearchWindow view, Func<IntPtr, Task> loadMenuItems, Action exitActionsMode)
    {
        _view = view;
        _loadMenuItems = loadMenuItems;
        _exitActionsMode = exitActionsMode;
    }

    public string? CurrentSubMenuTitle => _menuTitleStack.Count > 0 ? _menuTitleStack.Peek() : null;

    public void Reset()
    {
        _navigationVersion++;
        _menuStack.Clear();
        _menuSelectedIndexStack.Clear();
        _menuTitleStack.Clear();
    }

    public void NavigateActionsList(int direction)
    {
        var count = _view.LstActions.Items.Count;
        if (count == 0) return;
        var next = ListSelectionNavigator.NextSelectable(_view.LstActions.SelectedIndex, direction, count,
            i => _view.LstActions.Items[i] is ActionMenuItem item && !item.IsSeparator && !item.IsSectionHeader && !item.IsDisabled);
        if (next < 0) return;

        _view.LstActions.SelectedIndex = next;
        _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
    }

    public void EnterSubMenu() => _ = EnterSubMenuAsync();

    private async Task EnterSubMenuAsync()
    {
        if (_view.LstActions.SelectedItem is ActionMenuItem item && item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            _navigationVersion++;
            _menuStack.Push(item.SubMenuHandle);
            _menuSelectedIndexStack.Push(_view.LstActions.SelectedIndex);
            _menuTitleStack.Push(item.Text);
            FocusActionSearch();
            await _loadMenuItems(item.SubMenuHandle);
        }
    }

    public void GoBackMenuOrExit() => _ = GoBackMenuOrExitAsync();

    private async Task GoBackMenuOrExitAsync()
    {
        var version = ++_navigationVersion;
        if (_menuStack.Count > 0)
        {
            _menuStack.Pop();
            if (_menuTitleStack.Count > 0) _menuTitleStack.Pop();
            var parentMenu = _menuStack.Count > 0 ? _menuStack.Peek() : IntPtr.Zero;
            var prevIndex = _menuSelectedIndexStack.Count > 0 ? _menuSelectedIndexStack.Pop() : -1;
            FocusActionSearch();
            await _loadMenuItems(parentMenu);
            if (version == _navigationVersion)
            {
                if (prevIndex >= 0 && prevIndex < _view.LstActions.Items.Count)
                {
                    _view.LstActions.SelectedIndex = prevIndex;
                    _view.LstActions.ScrollIntoView(_view.LstActions.SelectedItem);
                }
            }
        }
        else _exitActionsMode();
    }

    private void FocusActionSearch()
    {
        var searchBox = _view.UsesFloatingActionsMenu ? _view.ActionsSearchTextBox : _view.SearchTextBox;
        searchBox.Clear();
        searchBox.Focus();
        Keyboard.Focus(searchBox);
    }
}
