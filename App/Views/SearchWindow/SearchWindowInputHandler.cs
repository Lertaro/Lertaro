using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.Helpers;
using Lertaro.App.Views.Controls.Results;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;
namespace Lertaro.App.Views.SearchWindow;

public class SearchWindowInputHandler
{
    private readonly Lertaro.App.SearchWindow _window;
    private readonly SearchWindowColumnActivation _columnActivation;

    public SearchWindowInputHandler(Lertaro.App.SearchWindow window)
    {
        _window = window;
        _columnActivation = new SearchWindowColumnActivation(window);
    }

    public void HandleWindowPreviewKeyDown(KeyEventArgs e)
    {
        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

        if (SearchWindowStayOpenSupport.TryHandle(_window, e))
            return;

        // Normal mode keys
        if (Keyboard.FocusedElement == _window.TxtSearchBoxControl &&
            (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Enter))
        {
            HandleTxtSearchBoxKeyDown(e);
            return;
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (string.IsNullOrEmpty(_window.TxtSearchBoxControl.Text))
            {
                _window.Close();
            }
            else
            {
                _window.TxtSearchBoxControl.Text = string.Empty;
                _window.TxtSearchBoxControl.Focus();
            }

            e.Handled = true;
            return;
        }

        // The Menu/Application key mirrors right-click and enters the same in-window floating actions
        // panel for keyboard users.
        if (e.Key == Key.Apps)
        {
            _window.MenuPresenter?.EnterActionsMode(GetSelectedResults());
            e.Handled = true;
            return;
        }
    }

    public void HandleWindowPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_window.MenuPresenter?.IsInActionsMode != true
            || _window.ResultsPanelControl.ActionsFlyoutHost.IsMouseOver)
            return;

        if (e.ChangedButton == MouseButton.Right
            && ResultsControl.FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject)?.Content is AppSearchResult result
            && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            return;

        _window.MenuPresenter.ExitActionsMode();
    }

    // WPF's ContextMenuService reacts to the Menu/Application key independently of the handler above --
    // its class handler runs on PreviewKeyUp with handledEventsToo:true, so setting e.Handled on KeyDown
    // above doesn't suppress it -- and opens the search box's own default Cut/Copy/Paste ContextMenu at
    // the same time as our action panel. CursorLeft/CursorTop are both -1 specifically when this event
    // was raised by a keyboard invocation (Apps key / Shift+F10) rather than an actual right-click, so
    // this only suppresses the redundant native menu for that keyboard case, and only when our own
    // flyout has something to show instead; a real right-click on the search box (e.g. to paste) is
    // unaffected.
    public void HandleSearchBoxContextMenuOpening(ContextMenuEventArgs e)
    {
        if (e.CursorLeft == -1 && e.CursorTop == -1
            && _window.LstGridResultsControl.SelectedItem is AppSearchResult
            && _window.MenuPresenter?.CanShowActionsMenu(GetSelectedResults()) == true)
        {
            e.Handled = true;
        }
    }

    public void HandleTxtSearchBoxKeyDown(KeyEventArgs e)
    {
        var actualKey = WpfUiHelper.GetActualKey(e);
        // Down/Up require no modifiers so a future combo hotkey sharing either base key (this window
        // doesn't wire any today, but QuickSearchWindow's equivalent does) wouldn't get shadowed here.
        if (actualKey == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveSelection(1);
            e.Handled = true;
        }

        else if (actualKey == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveSelection(-1);
            e.Handled = true;
        }

        else if (actualKey == Key.Enter)
        {
            // File/folder results are handled earlier by HotkeyActionTrigger (Ctrl+Enter locate,
            // Ctrl+Shift+Enter open-as-admin) and never reach here. What reaches here on those chords
            // is a result with no matching file action — notably an application — so honor
            // Ctrl+Shift+Enter as "launch as admin" so apps can still be elevated.
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                OpenSelectedResult(asAdmin: asAdmin);
            }
            e.Handled = true;
        }
    }

    private IReadOnlyList<AppSearchResult> GetSelectedResults() =>
        _window.LstGridResultsControl.SelectedItems.Cast<object>().OfType<AppSearchResult>().ToList();

    public void HandleLstGridResultsMouseDoubleClick(MouseButtonEventArgs e)
    {
        // Control.MouseDoubleClickEvent fires for ANY button's double-click (left, right, or middle),
        // not just left -- without this guard, double-right-clicking a row both opens the action flyout
        // (via PreviewMouseRightButtonUp) AND opens the file/folder itself.
        if (e.ChangedButton != MouseButton.Left)
            return;

        var depObj = e.OriginalSource as DependencyObject;
        while (depObj != null && !(depObj is ListViewItem))
        {
            if (depObj is GridViewColumnHeader)
            {
                return; // Ignore double clicks on column headers!
            }

            depObj = SearchWindowColumnActivation.VisualOrContentParent(depObj);
        }

        if (depObj is ListViewItem item && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var isFileOrFolder = SearchWindowColumnActivation.IsFileOrFolder(result);

            if (!_columnActivation.TryHandle(e, item, result, isFileOrFolder))
            {
                FileExecutor.OpenFileOrFolder(result.FullPath);
            }
        }
    }

    internal static string? ResolveColumnIdAtX(double x, IEnumerable<(string ColumnId, double Width)> columns) =>
        SearchWindowColumnActivation.ResolveColumnIdAtX(x, columns);

    // Ctrl+Enter (locate in Explorer) and Ctrl+Shift+Enter (open as admin) are NOT handled here --
    // they're registered action hotkeys (LocateInExplorerAction/OpenResultAsAdminAction) dispatched by
    // SearchInputHelper.TryActionHotkey during the window's tunneling PreviewKeyDown, which runs before
    // this list's own bubbling KeyDown ever sees the event and marks it handled. A modifier+Enter case
    // used to be duplicated here too, but it could never actually run.
    public void HandleLstGridResultsKeyDown(KeyEventArgs e)
    {
        var actualKey2 = WpfUiHelper.GetActualKey(e);
        if (actualKey2 == Key.Enter)
        {
            if (_window.LstGridResultsControl.SelectedItem is AppSearchResult)
            {
                OpenSelectedResult(asAdmin: false);
            }
            e.Handled = true;
        }
    }

    public void OpenSelectedResult(bool asAdmin = false)
    {
        // Open every selected result (the grid list allows multi-selection).
        var opened = 0;
        foreach (var obj in _window.LstGridResultsControl.SelectedItems)
        {
            if (obj is AppSearchResult r && !r.IsSearchSectionHeader && !r.IsEmptyResult && !string.IsNullOrEmpty(r.FullPath))
            {
                if (asAdmin)
                    FileExecutor.OpenFileOrFolderAsAdmin(r.FullPath);
                else
                    FileExecutor.OpenFileOrFolder(r.FullPath);
                opened++;
            }
        }

        if (opened == 0 && _window.LstGridResultsControl.SelectedItem is AppSearchResult selected && !string.IsNullOrEmpty(selected.FullPath))
        {
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(selected.FullPath);
            else
                FileExecutor.OpenFileOrFolder(selected.FullPath);
        }
    }

    // Wraps at both ends, and skips the rows that exist only to be looked at, the same way the quick,
    // inline and actions lists already do -- this window was the one still clamping at the first and
    // last row. ListSelectionNavigator also declines to move when nothing else is selectable, so a list
    // holding a single result no longer re-selects and re-scrolls it on every key press.
    private void MoveSelection(int delta)
    {
        var count = _window.LstGridResultsControl.Items.Count;
        if (count == 0)
        {
            _window.LstGridResultsControl.SelectedIndex = -1;
            return;
        }

        var next = ListSelectionNavigator.NextSelectable(_window.LstGridResultsControl.SelectedIndex, delta, count,
            i => _window.LstGridResultsControl.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader);
        if (next < 0)
            return;

        _window.LstGridResultsControl.SelectedIndex = next;
        _window.LstGridResultsControl.ScrollIntoView(_window.LstGridResultsControl.SelectedItem);
    }

    public void HandleLstGridResultsPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not ListViewItem)
        {
            element = SearchWindowColumnActivation.VisualOrContentParent(element);
        }

        if (element is ListViewItem listViewItem && listViewItem.Content is AppSearchResult result)
        {
            e.Handled = true;
            // Preserve an existing multi-selection when right-clicking one of its members;
            // otherwise select just the right-clicked item.
            if (!_window.LstGridResultsControl.SelectedItems.Contains(result))
                _window.LstGridResultsControl.SelectedItem = result;

            _window.MenuPresenter?.EnterActionsMode(GetSelectedResults());
        }
    }
}
