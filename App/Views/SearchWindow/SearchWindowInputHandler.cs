using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.Helpers;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Plugin;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
namespace Lertaro.App.Views.SearchWindow;

public class SearchWindowInputHandler
{
    private readonly Lertaro.App.SearchWindow _window;

    public SearchWindowInputHandler(Lertaro.App.SearchWindow window) => _window = window;

    public void HandleWindowPreviewKeyDown(KeyEventArgs e)
    {
        // While the action flyout is open it owns navigation; still let action hotkeys fire on the item
        // (Ctrl+C etc.), then stand down so arrows/enter drive the flyout, not the result list behind it.
        if (ActionFlyout.IsOpen)
        {
            if (SearchInputHelper.TryActionHotkey(e, _window, _window.MenuPresenter))
                ActionFlyout.Close();
            return;
        }

        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
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

        // The Menu/Application key mirrors right-click, same as Explorer and other Windows apps: opens
        // the action flyout for whatever's currently selected. Reuses the exact same PlacementMode.
        // MousePoint right-click uses -- WPF's Popup reads the current system cursor position itself for
        // that mode, so this needs no coordinate math of its own; the pointer just happens to still be
        // sitting wherever it last was (typically on/near the selected row) instead of mid-click. Other
        // keyboard access to actions is via the registered action hotkeys (Ctrl+C, Ctrl+Enter, ...),
        // handled directly on the item by HandleCommonSearchKeys above.
        if (e.Key == Key.Apps)
        {
            ShowActionFlyout(PlacementMode.MousePoint);
            e.Handled = true;
            return;
        }
    }

    // WPF's ContextMenuService reacts to the Menu/Application key independently of the handler above --
    // its class handler runs on PreviewKeyUp with handledEventsToo:true, so setting e.Handled on KeyDown
    // above doesn't suppress it -- and opens the search box's own default Cut/Copy/Paste ContextMenu at
    // the same time as our action flyout. CursorLeft/CursorTop are both -1 specifically when this event
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

    private IReadOnlyList<AppSearchResult> GetSelectedResults()
    {
        var list = new List<AppSearchResult>();
        foreach (var obj in _window.LstGridResultsControl.SelectedItems)
            if (obj is AppSearchResult r) list.Add(r);
        return list;
    }

    // Mouse events can originate from a non-Visual ContentElement (e.g. a highlight Run inside a result's
    // name TextBlock); VisualTreeHelper.GetParent throws on those, so step to the content parent instead.
    private static DependencyObject? VisualOrContentParent(DependencyObject dep)
        => dep is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(dep)
            : (dep as FrameworkContentElement)?.Parent;

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

            depObj = VisualOrContentParent(depObj);
        }

        if (depObj is ListViewItem item && item.Content is AppSearchResult result)
        {
            e.Handled = true;
            var isFileOrFolder = !result.IsSearchSectionHeader && !result.IsEmptyResult &&
                (result.ResultKind == "File" || result.ResultKind == "Folder" || System.IO.File.Exists(result.FullPath) || System.IO.Directory.Exists(result.FullPath));

            if (!TryHandleColumnDoubleClick(e, item, result, isFileOrFolder))
            {
                FileExecutor.OpenFileOrFolder(result.FullPath);
            }
        }
    }

    // A double-click on a specific column can behave differently than double-clicking the row
    // generally -- e.g. the built-in Path column opens the containing folder instead of the result
    // itself, mirroring Everything's own results grid.
    private bool TryHandleColumnDoubleClick(MouseButtonEventArgs e, ListViewItem item, AppSearchResult result, bool isFileOrFolder)
    {
        var columnId = GetClickedColumnId(e, item);
        if (string.IsNullOrEmpty(columnId))
            return false;

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            foreach (var column in provider.GetColumns())
            {
                if (column.ColumnId == columnId && column.OnDoubleClick != null)
                {
                    column.OnDoubleClick(result);
                    return true;
                }
            }
        }

        if (columnId == "Path" && isFileOrFolder)
        {
            // This is just the Path column's own default action, same standing as double-clicking the
            // Name column opening the file -- it doesn't close the window either.
            FileExecutor.LocateInExplorer(result.FullPath);
            return true;
        }

        return false;
    }

    // GridView has no built-in "which cell was clicked" API (unlike GridViewColumnHeader for header
    // clicks) -- GridViewRowPresenter lays columns out left-to-right by ActualWidth, so the clicked
    // column is whichever one the X position (relative to the row) falls into.
    private string? GetClickedColumnId(MouseButtonEventArgs e, ListViewItem item)
    {
        if (_window.LstGridResultsControl.View is not GridView gridView)
            return null;

        var columns = gridView.Columns.Cast<GridViewColumn>()
            .Select(c => (ColumnId: ColumnIdentity.GetId(c), c.ActualWidth));
        return ResolveColumnIdAtX(e.GetPosition(item).X, columns);
    }

    // Pulled out of GetClickedColumnId as a pure function (no live GridView needed) so the actual
    // hit-testing math is unit-testable -- GetClickedColumnId itself needs a real, laid-out GridView to
    // construct a call to it at all.
    internal static string? ResolveColumnIdAtX(double x, IEnumerable<(string ColumnId, double Width)> columns)
    {
        double cumulativeWidth = 0;
        foreach (var (columnId, width) in columns)
        {
            cumulativeWidth += width;
            if (x < cumulativeWidth)
                return columnId;
        }

        return null; // clicked past the last column, in leftover row width
    }

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
            element = VisualOrContentParent(element);
        }

        if (element is ListViewItem listViewItem && listViewItem.Content is AppSearchResult result)
        {
            e.Handled = true;
            // Preserve an existing multi-selection when right-clicking one of its members;
            // otherwise select just the right-clicked item.
            if (!_window.LstGridResultsControl.SelectedItems.Contains(result))
                _window.LstGridResultsControl.SelectedItem = result;

            // Show the action flyout at the cursor. Anchored to the LIST, not to the row's own
            // container -- see ShowActionFlyout. MousePoint positions against the pointer, so the row
            // never contributed anything here anyway.
            ShowActionFlyout(PlacementMode.MousePoint, _window.LstGridResultsControl);
        }
    }

    // Opens the action flyout for the current selection. Gated by the same CanShowActionsMenu check the
    // old in-window actions panel used, so apps / plugin results / empty rows still suppress it.
    //
    // The anchor is never a row's own container. A Popup dies with its PlacementTarget, and the results
    // list virtualizes with recycling, so a container is torn down and rebuilt whenever the collection
    // changes -- which for a search that is still streaming is every couple of hundred milliseconds. The
    // flyout closed by itself while the rows it was opened over sat there unchanged, because what went
    // away was the container and not the selection. Anchoring to the list instead costs nothing:
    // MousePoint places the popup against the pointer and ignores the target, and the one placement that
    // does use the target (Bottom, the fallback below) uses the search box.
    private void ShowActionFlyout(PlacementMode placement, UIElement? anchor = null)
    {
        var selection = GetSelectedResults();
        if (_window.MenuPresenter?.CanShowActionsMenu(selection) != true)
            return;

        if (anchor == null)
        {
            // Keyboard-triggered. Scroll the selected row into view first so the flyout opens next to
            // something the user can see; if it still isn't realized after that, fall back to the search
            // box so the flyout is always visible rather than off the bottom of the list.
            var lst = _window.LstGridResultsControl;
            var selected = lst.SelectedItem;
            if (selected != null)
            {
                lst.ScrollIntoView(selected);
                lst.UpdateLayout();
            }

            // The container is asked about, but only to tell whether the row is on screen -- it is not
            // what gets anchored to.
            var rowIsRealized = selected != null && lst.ItemContainerGenerator.ContainerFromItem(selected) != null;
            if (rowIsRealized)
            {
                anchor = lst;
            }
            else
            {
                anchor = _window.TxtSearchBoxControl;
                placement = PlacementMode.Bottom;
            }
        }

        ActionFlyout.Show(selection, _window, _window, anchor, placement);
    }
}
