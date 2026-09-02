using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowInputHandler
{
    private readonly Lertaro.App.QuickSearchWindow _window;

    public QuickSearchWindowInputHandler(Lertaro.App.QuickSearchWindow window) => _window = window;

    public void HandleWindowPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (_window.MenuPresenter?.IsInActionsMode != true
            || _window.ResultsPanelControl.ActionsFlyoutHost.IsMouseOver)
            return;

        if (e.ChangedButton == MouseButton.Right
            && Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject)?.Content is AppSearchResult result
            && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            return;

        _window.MenuPresenter.ExitActionsMode();
    }

    public void HandleWindowPreviewKeyDown(KeyEventArgs e)
    {
        if (QuickSearchLaunchPanelInputHelper.Handle(e, _window))
            return;

        if (SearchInputHelper.HandleCommonSearchKeys(e, _window, _window.MenuPresenter))
            return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            _window.HideWindow();
            e.Handled = true;
            return;
        }

        var settings = UserSettings.Load().Hotkeys;
        if (WpfUiHelper.MatchesHotkey(settings.CompleteFromSelectionHotkey, Keyboard.Modifiers, WpfUiHelper.GetActualKey(e)))
        {
            CompleteSearchFromSelection();
            e.Handled = true;
            return;
        }
        // Modifiers == None guards this against Ctrl+Right (and any other Right combo), which should
        // fall through to whatever owns that combo instead of always opening the actions menu just
        // because the underlying key happens to be the same.
        if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.None && SearchInputHelper.IsSearchCaretAtEnd(_window))
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result)
            {
                if (result.IsSearchSectionHeader)
                {
                    e.Handled = true;
                    return;
                }
                _window.MenuPresenter?.EnterActionsMode(result);
                e.Handled = true;
                return;
            }
        }
        var actualKey = WpfUiHelper.GetActualKey(e);
        if (WpfUiHelper.MatchesHotkey(settings.KeywordHistoryPreviousHotkey, Keyboard.Modifiers, actualKey))
        {
            _window.KeywordHistoryController.Navigate(previous: true);
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.KeywordHistoryNextHotkey, Keyboard.Modifiers, actualKey))
        {
            _window.KeywordHistoryController.Navigate(previous: false);
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.KeywordHistoryDeleteHotkey, Keyboard.Modifiers, actualKey))
        {
            _window.KeywordHistoryController.DeleteCurrent();
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.StayOpenHotkey, Keyboard.Modifiers, actualKey))
        {
            // Keeps this summon on screen when focus goes elsewhere, so a query can be assembled from
            // text copied out of other windows. See QuickSearchWindowController.ToggleStayOpen.
            _window.ToggleStayOpen();
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.OpenFullWindowHotkey, Keyboard.Modifiers, actualKey))
        {
            // Opens the full SearchWindow directly (bypassing the search box logo's menu detour), carrying
            // over whatever query is currently active (including a saved query while in actions mode) --
            // same query-carrying behavior ShowTrayMenu gets to via its "Show Main Window" item. The full
            // window has no concept of a per-type trigger, so one is stripped before it ever gets there.
            var queryText = (_window.IsInActionsMode && _window.MenuPresenter != null) ? _window.MenuPresenter.SavedSearchQuery : _window.TxtSearch.Text;
            FileExecutor.OpenFileOrFolder("__SHOW_MORE__", SearchResultTypePriority.StripLeadingTrigger(queryText), _window.HideWindowNoRestore);
            e.Handled = true;
            return;
        }
        if (UserSettings.Load().LocalSend.Enabled && WpfUiHelper.MatchesHotkey(settings.LocalSendSendWindowHotkey, Keyboard.Modifiers, actualKey))
        {
            Lertaro.App.Helpers.LocalSend.LocalSendAppEventHandler.OpenSendWindow();
            e.Handled = true;
            return;
        }
        if (actualKey == Key.Enter)
        {
            // An actively-composing IME (e.g. Sogou/Microsoft Pinyin in "Enter commits the raw typed
            // code" mode) reports this key as Key.ImeProcessed with ImeProcessedKey == Key.Enter, not
            // a plain Key.Enter -- GetActualKey unwraps that identically to a real Enter press, so
            // without this guard the composition text never reaches the TextBox and whatever result
            // happens to be selected gets executed instead (#125). Let it fall through unhandled so
            // WPF's normal IME pipeline commits the composition into the search box, same as it would
            // if this handler didn't exist at all.
            if (e.Key == Key.ImeProcessed)
            {
                return;
            }

            var result = _window.LstResults.SelectedItem as AppSearchResult;
            if (result == null && _window.LstResults.Items.Count > 0)
            {
                _window.LstResults.SelectedIndex = 0;
                result = _window.LstResults.SelectedItem as AppSearchResult;
            }

            // File/folder results are handled earlier by HotkeyActionTrigger (Ctrl+Enter locate,
            // Ctrl+Shift+Enter open-as-admin) and never reach here. What reaches here on those chords
            // is a result with no matching file action — notably an application — so honor
            // Ctrl+Shift+Enter as "launch as admin" so apps can still be elevated.
            if (result != null)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                ExecuteResult(result, asAdmin: asAdmin);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down)
        {
            MoveResultSelection(1);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Up)
        {
            MoveResultSelection(-1);
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, Keyboard.Modifiers, actualKey))
        {
            MoveResultSelection(1);
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
        {
            MoveResultSelection(-1);
            e.Handled = true;
            return;
        }
        if (WpfUiHelper.MatchesHotkey(settings.ActionsMenuHotkey, Keyboard.Modifiers, actualKey))
        {
            if (_window.LstResults.SelectedItem is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
            {
                _window.MenuPresenter?.EnterActionsMode(result);
                e.Handled = true;
                return;
            }
        }

        if (!string.IsNullOrEmpty(settings.SelectJumpModifier) && Keyboard.Modifiers == WpfUiHelper.GetWpfModifier(settings.SelectJumpModifier))
        {
            var num = -1;
            if (actualKey >= Key.D1 && actualKey <= Key.D9)
                num = actualKey - Key.D1;
            else if (actualKey >= Key.NumPad1 && actualKey <= Key.NumPad9)
                num = actualKey - Key.NumPad1;
            if (num >= 0)
            {
                var scrollViewer = WpfUiHelper.GetScrollViewer(_window.LstResults);
                // Mode-aware (see QuickSearchShortcutHelper's own comment) -- this is the separate
                // execution-side lookup that maps the pressed digit back to an actual result.
                var firstVisible = WpfUiHelper.GetFirstVisibleIndex(scrollViewer, UiMetrics.ScaledNormalRowHeight);
                var shortcutIndex = 0;
                for (var i = firstVisible; i < _window.LstResults.Items.Count; i++)
                {
                    if (_window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader)
                    {
                        if (shortcutIndex == num)
                        {
                            ExecuteResult(item, asAdmin: false);
                            e.Handled = true;
                            break;
                        }
                        shortcutIndex++;
                    }
                }
            }
        }
    }

    private void CompleteSearchFromSelection()
    {
        var result = _window.LstResults.SelectedItem as AppSearchResult;
        if (result == null && _window.LstResults.Items.Count > 0)
        {
            result = _window.LstResults.Items[0] as AppSearchResult;
        }
        if (result == null || result.IsEmptyResult || result.FullPath == "__SHOW_MORE__" || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }
        var completion = GetCompletionText(result);
        if (string.Equals(_window.TxtSearch.Text, completion, StringComparison.Ordinal))
        {
            return;
        }
        _window.TxtSearch.Text = completion;
        _window.TxtSearch.CaretIndex = _window.TxtSearch.Text.Length;
        _window.TxtSearch.Focus();
    }
    private void ExecuteResult(AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsEmptyResult || result.IsSearchSectionHeader)
            return;
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(_window.TxtSearch.Text, result.FullPath, SearchResultHelper.HistoryKindOf(result));
        }
        if (result.IsPluginSearchAction)
        {
            PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin);
            return;
        }
        if (PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin))
        {
            return;
        }
        var currentQuery = _window.TxtSearch.Text;
        if (result.FullPath == "__SHOW_MORE__")
        {
            _window.HideWindowNoRestore();
            FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, _window.HideWindowNoRestore);
        }
        else
        {
            _window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, _window.HideWindow);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, _window.HideWindow);
        }
    }
    private void MoveResultSelection(int direction)
    {
        // Wraps like the actions list's NavigateActionsList (ShellMenuPresenter.cs) -- past the last
        // item goes back to the first, and vice versa.
        var count = _window.LstResults.Items.Count;
        if (count == 0) return;
        var next = ListSelectionNavigator.NextSelectable(_window.LstResults.SelectedIndex, direction, count,
            i => _window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader);
        if (next < 0) return;

        _window.LstResults.SelectedIndex = next;
        _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
    }
    private static string GetCompletionText(AppSearchResult result)
    {
        if (result.IsInstantResult)
        {
            if (!string.IsNullOrWhiteSpace(result.TabCompletion))
                return result.TabCompletion;
            return result.InstantResultActionArgument;
        }
        if (result.IsApplication)
        {
            return result.Name;
        }
        var path = result.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return result.Name;
        }
        return path;
    }
}
