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

    /// <summary>
    /// Which way one keypress moves the category filter bar, or 0 when the key is not the configured one.
    /// </summary>
    /// <remarks>
    /// The recorded value is the forward direction and the same key with its Shift bit flipped is the reverse,
    /// which is what bare Tab and Shift+Tab have always meant here -- and it keeps that pair intact for a user
    /// who records Ctrl+Tab instead of leaving them with a forward key and no way back. Exact modifier matching
    /// is what stops a configured Tab from also eating the Ctrl+Tab that completion is holding.
    /// </remarks>
    internal static int CategoryCycleDelta(string? hotkey, ModifierKeys current, Key key)
    {
        if (!WpfUiHelper.TryParseHotkey(hotkey, out var boundKey, out var modifiers)) return 0;
        if (key != boundKey) return 0;
        if (modifiers == current) return 1;
        return (modifiers ^ ModifierKeys.Shift) == current ? -1 : 0;
    }

    public void HandleWindowPreviewKeyDown(KeyEventArgs e)
    {
        // The category filter bar is cycled by a key of the user's own (Tab by default) once there is
        // something to filter. Before any input it is left to
        // the code below, and this has to run BEFORE HandleCommonSearchKeys because that method claims bare
        // Tab unconditionally: it calls EnterSubMenu and then marks the key handled and returns. Left behind
        // that call, the category switch below would never run.
        //
        // Actions mode keeps its own Tab: there the list really does hold ActionMenuItem entries, so
        // EnterSubMenu descends into a submenu (ActionsMenuNavigator) and swallowing the key would take that
        // away whenever the search box still holds the query -- which is always, in this window.
        //
        // Gated on the box having text: before any input the card belongs to the launch panel, and the wheel
        // over the bar is not what a user there is reaching for.
        //
        // An empty setting stands this whole branch down: the key then reaches HandleCommonSearchKeys, which
        // is what it did before the filter bar existed.
        var cycleDelta = !_window.IsInActionsMode && _window.HasSearchText
            ? CategoryCycleDelta(UserSettings.Load().Hotkeys.CycleCategoryHotkey, Keyboard.Modifiers, WpfUiHelper.GetActualKey(e))
            : 0;
        if (cycleDelta != 0)
        {
            _window.CycleCategory(cycleDelta);
            e.Handled = true;
            return;
        }

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
            _window.RecordKeywordHistory();
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
            // Non-file rows only reach here (HotkeyActionTrigger already handled files -- see its header),
            // so Ctrl+Shift+Enter stays "launch as admin" for apps.
            if (result != null)
            {
                var asAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
                var current = SearchResultExecutionHelper.ResolveCurrent(result, _window.TxtSearch.Text, isInlineWindow: false);
                if (current != null)
                    ExecuteResult(current, asAdmin);
            }
            e.Handled = true;
            return;
        }
        // An active IME reports even a bare arrow as Key.ImeProcessed, so these read the unwrapped
        // actualKey like the hotkeys below do. The no-modifier guard keeps Ctrl/Alt+arrow falling
        // through to that dispatch instead of moving the selection.
        if (actualKey == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            MoveResultSelection(1);
            e.Handled = true;
            return;
        }
        if (actualKey == Key.Up && Keyboard.Modifiers == ModifierKeys.None)
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
                // Same scrolling-mode-tolerant helper the hints use (see QuickSearchShortcutHelper) -- this
                // is the separate execution-side lookup that maps the pressed digit back to an actual result.
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
        _window.RecordKeywordHistory();
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(
                SearchResultHelper.HistoryKeywordOf(result, _window.TxtSearch.Text),
                result.FullPath,
                SearchResultHelper.HistoryKindOf(result),
                SearchResultHelper.IsLearnedRecall(result));
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
        // Wraps past either end -- see ListSelectionNavigator.NextSelectable for the rule.
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
