using System.Windows;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Helpers;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowInputHandler
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private readonly InlineSearchWindowLayoutManager _layoutManager;
    private readonly InlineExplorerSelectionSync _explorerSelection;
    private bool _userNavigatedSinceLastQuery;

    public void ResetUserNavigation() => _userNavigatedSinceLastQuery = false;

    public InlineSearchWindowInputHandler(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _layoutManager = new InlineSearchWindowLayoutManager(window);
        _explorerSelection = new InlineExplorerSelectionSync(window);
    }

    public void HandlePreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

        // Every bare key check below (including Escape) requires no modifiers -- otherwise it would
        // shadow a user-configurable combo hotkey sharing the same base key (e.g. CompleteFromSelectionHotkey
        // defaults to Ctrl+Tab) before it
        // ever reaches that hotkey's own dispatch further down (or the calling window's).
        var noModifiers = Keyboard.Modifiers == ModifierKeys.None;

        if (e.Key == Key.Tab && noModifiers)
        {
            e.Handled = true;
            return;
        }

        // Escape key
        if (e.Key == Key.Escape && noModifiers)
        {
            e.Handled = true;
            ExitSearch();
            return;
        }

        // Backspace in an already-empty box: same "leave the search" intent as Escape. Nothing would be
        // edited (there is no text to delete), so the key is free to mean "exit" instead of doing nothing.
        // Deliberately IsNullOrEmpty and not IsNullOrWhiteSpace: a lone space IS editable content, so
        // backspace on " " must still delete it rather than exit. Placed after HandleCommonSearchKeys above
        // so an open actions menu keeps treating Backspace as "go back a level" first.
        if (e.Key == Key.Back && noModifiers && string.IsNullOrEmpty(_window.SearchTextBox.Text))
        {
            e.Handled = true;
            ExitSearch();
            return;
        }

        // Enter key
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actualKey == Key.Enter)
        {
            e.Handled = true;

            // An empty box shows the current folder's contents as a browsing list, not search results,
            // so there is no top match for Enter to "open" -- running whichever child happens to be
            // selected is not what pressing Enter on nothing means. Exit the search instead, exactly as
            // Escape does. Same emptiness test as SearchDispatchController's, so whitespace-only counts
            // as empty here just like it does when deciding whether to run a search at all.
            if (string.IsNullOrWhiteSpace(_window.SearchTextBox.Text))
            {
                ExitSearch();
                return;
            }

            var result = InlineResultTargetResolver.ResolveEnterTarget(
                _window.LstResults.Items.OfType<AppSearchResult>().ToList(),
                _window.LstResults.SelectedIndex);

            // File/folder results are handled earlier by HotkeyActionTrigger (Ctrl+Enter locate,
            // Ctrl+Shift+Enter open-as-admin) and never reach here. What reaches here on those chords
            // is a result with no matching file action — notably an application — so honor
            // Ctrl+Shift+Enter as "launch as admin" so apps can still be elevated.
            if (result != null)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                var current = SearchResultExecutionHelper.ResolveCurrent(result, _window.SearchTextBox.Text, isInlineWindow: true);
                if (current != null)
                    ExecuteResult(current, asAdmin);
            }
            return;
        }

        // Up arrow
        if (actualKey == Key.Up && noModifiers)
        {
            e.Handled = true;
            MoveResultSelection(-1);
            return;
        }

        // Down arrow
        if (actualKey == Key.Down && noModifiers)
        {
            e.Handled = true;
            MoveResultSelection(1);
            return;
        }

        // Right arrow -- only opens Actions when the caret is already at the end of the query, so it
        // doesn't hijack normal text-cursor movement while editing earlier in the search box.
        if (e.Key == Key.Right && noModifiers && SearchInputHelper.IsSearchCaretAtEnd(_window))
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result)
            {
                e.Handled = true;
                _window.MenuPresenter.EnterActionsMode(result);
            }
            return;
        }

        // Next/previous item + actions menu + jump-to-item shortcuts

        var settings = UserSettings.Load().Hotkeys;
        if (WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, Keyboard.Modifiers, actualKey))
        {
            e.Handled = true;
            MoveResultSelection(1);
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
        {
            e.Handled = true;
            MoveResultSelection(-1);
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.ActionsMenuHotkey, Keyboard.Modifiers, actualKey))
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                e.Handled = true;
                _window.MenuPresenter.EnterActionsMode(result);
                return;
            }
        }

        if (!string.IsNullOrEmpty(settings.SelectJumpModifier) && Keyboard.Modifiers == WpfUiHelper.GetWpfModifier(settings.SelectJumpModifier))
        {
            var num = -1;
            if (actualKey >= Key.D1 && actualKey <= Key.D9)
                num = (int)actualKey - (int)Key.D1 + 1;
            else if (actualKey >= Key.NumPad1 && actualKey <= Key.NumPad9)
                num = (int)actualKey - (int)Key.NumPad1 + 1;
            if (num >= 1 && num <= 9)
            {
                e.Handled = true;
                LaunchByShortcutIndex(num);
                return;
            }
        }
    }

    public void QueueResultsLayoutUpdate() => _layoutManager.QueueResultsLayoutUpdate();
    public void UpdateActionsLayout() => _layoutManager.UpdateActionsLayout();

    // Shared by Escape and by Enter-on-empty-box: leaving the search means the same thing in both cases,
    // so they must not drift apart. Inside an Explorer file dialog the window stays up (the dialog is the
    // thing the user was typing into) and focus simply returns to it; otherwise the inline window closes.
    private void ExitSearch()
    {
        if (_window.Manager.ExplorerTracker.IsActiveWindowDialog)
            _window.ResetInlineSearchAndFocusDialog();
        else
            _window.HideWindow();
    }

    public void UpdateShortcutHints() => _layoutManager.UpdateShortcutHints();

    public void UpdatePathPreviewVisibility() => _layoutManager.UpdatePathPreviewVisibility();

    public void LaunchByShortcutIndex(int num) => InlineSearchShortcutLauncher.Launch(_window, _layoutManager, num);

    public void SyncExplorerSelection() => _explorerSelection.Sync();

    // Explicit user navigation must reach the host even mid-refresh -- see the collaborator's own method.
    public void SyncExplorerSelectionAfterNavigation() => _explorerSelection.SyncAfterNavigation();

    public void SuppressExplorerSelectionSyncForResultRefresh() => _explorerSelection.BeginResultRefresh(AutoSelectFirstResultIfNeeded);

    private static bool IsSelectableResult(AppSearchResult? item) =>
        item != null && !item.IsEmptyResult && !item.IsSearchSectionHeader;

    private void AutoSelectFirstResultIfNeeded()
    {
        // Do not disturb a selection the user made themselves.
        if (_userNavigatedSinceLastQuery)
            return;

        // What to land on is decided by InlineResultTargetResolver: a real file/folder is preferred even
        // when a shortcut command or instant result also matched, because this row is the one Enter acts on
        // -- landing on a command made Enter run the command instead of opening the file the user searched
        // for. (Arrow keys still reach every row.)
        var rows = _window.LstResults.Items.OfType<AppSearchResult>().ToList();
        var index = InlineResultTargetResolver.ResolveAutoSelectIndex(rows);

        // A row that is already selected and still present needs no reselection; re-assigning would also
        // reset the scroll position on every streaming refresh.
        if (index < 0 || ReferenceEquals(rows[index], _window.LstResults.SelectedItem))
            return;

        _window.LstResults.SelectedItem = rows[index];
        _window.LstResults.ScrollIntoView(rows[index]);
    }

    private void MoveResultSelection(int direction)
    {
        // Wraps like the actions list's NavigateActionsList (ShellMenuPresenter.cs) -- past the last
        // item goes back to the first, and vice versa.
        _userNavigatedSinceLastQuery = true;
        var count = _window.LstResults.Items.Count;
        if (count == 0) return;
        var next = ListSelectionNavigator.NextSelectable(_window.LstResults.SelectedIndex, direction, count,
            i => IsSelectableResult(_window.LstResults.Items[i] as AppSearchResult));
        if (next < 0) return;

        _window.LstResults.SelectedIndex = next;
        _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);

        // The selected row changed by the user's own key press, so the host file manager must follow it
        // even mid-refresh (see SyncExplorerSelectionAfterNavigation).
        SyncExplorerSelectionAfterNavigation();
    }

    public static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject => InlineSearchWindowLayoutManager.FindVisualParent<T>(child);

    private void ExecuteResult(AppSearchResult result, bool asAdmin)
    {
        if (asAdmin)
            _window.ExecuteSearchResultAsAdmin(result);
        else
            _window.ExecuteSearchResult(result);
    }
}
