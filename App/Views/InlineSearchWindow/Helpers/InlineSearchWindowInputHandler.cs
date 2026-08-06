using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.Core;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

using Lertaro.Core.Wire;
namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

public class InlineSearchWindowInputHandler
{
    private readonly Lertaro.App.InlineSearchWindow _window;
    private readonly InlineSearchWindowLayoutManager _layoutManager;
    private bool _suppressExplorerSelectionSync;
    private bool _userNavigatedSinceLastQuery;
    private int _selectionSyncQueued;

    public void ResetUserNavigation() => _userNavigatedSinceLastQuery = false;

    public InlineSearchWindowInputHandler(Lertaro.App.InlineSearchWindow window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _layoutManager = new InlineSearchWindowLayoutManager(window);
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
            if (_window.Manager.ExplorerTracker.IsActiveWindowDialog)
            {
                _window.ResetInlineSearchAndFocusDialog();
            }
            else
            {
                _window.HideWindow();
            }
            return;
        }

        // Enter key
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (actualKey == Key.Enter)
        {
            e.Handled = true;

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

    public void UpdateShortcutHints() => _layoutManager.UpdateShortcutHints();

    public void UpdatePathPreviewVisibility() => _layoutManager.UpdatePathPreviewVisibility();

    public void LaunchByShortcutIndex(int num)
    {
        if (num < 1 || num > 9) return;
        var scrollViewer = _layoutManager.GetScrollViewer(_window.LstResults);
        // Mode-aware (see InlineSearchShortcutHelper's own comment) -- this is the separate
        // execution-side lookup that maps the pressed digit back to an actual result.
        var rowHeight = Math.Round(UiMetrics.SearchResultItemHeight * 0.7);
        var firstVisible = WpfUiHelper.GetFirstVisibleIndex(scrollViewer, rowHeight);
        var shortcutIndex = 1;
        for (var i = firstVisible; i < _window.LstResults.Items.Count; i++)
        {
            if (_window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader)
            {
                if (shortcutIndex == num)
                {
                    _window.ExecuteSearchResult(item);
                    return;
                }

                shortcutIndex++;
            }
        }
    }

    public void SyncExplorerSelection()
    {
        if (_suppressExplorerSelectionSync)
            return;
        if (_window.LstResults.SelectedItem is not AppSearchResult result) return;
        if (result.FullPath == "__SHOW_MORE__") return;
        var tracker = _window.Manager.ExplorerTracker;
        if (tracker.ActiveInlineAdapter != null && tracker.ActiveHwnd != IntPtr.Zero)
        {
            // Trailing separator marks a directory, same convention ExecuteItem's own IPC call already
            // uses (see InlineAdapterIpcCoordinator.NormalizePath) -- an adapter's OnSelectionChanged runs
            // in the Hook process, which can't reliably re-derive this itself (Directory.Exists on a
            // mapped network drive silently fails when the Hook is elevated into a different logon
            // session), so it has to travel with the path instead of being recomputed on the other end.
            var path = result.IsDir && !result.FullPath.EndsWith('\\') ? result.FullPath + "\\" : result.FullPath;
            App.HookClient?.SendMessage(new IpcMessage
            {
                Id = IpcMessageId.InlineSelectionChanged,
                Hwnd = tracker.ActiveHwnd.ToInt64(),
                StringVal1 = path
            });

            // Some adapters' OnSelectionChanged can activate the host file manager as a side effect
            // (XYplorer's goto/SelectItems) or as a hard requirement (TC's CD command needs to be
            // foreground to act at all) -- either way it steals keyboard focus mid-typing, eating even
            // Backspace. How long that takes (or whether it happens at all) is entirely host-specific, so
            // each adapter tunes its own SelectionSyncFocusReclaimDelayMs rather than sharing one
            // process-wide constant; 0 (the default) means this adapter never does this, so skip
            // scheduling entirely.
            var reclaimDelayMs = tracker.ActiveInlineAdapter.SelectionSyncFocusReclaimDelayMs;
            if (reclaimDelayMs > 0)
                ScheduleFocusReclaim(reclaimDelayMs);
        }
    }

    private DispatcherTimer? _reclaimFocusTimer;

    // Reset (not just started) on every call so rapid typing only reclaims once, after the last
    // selection change actually settles. Uses ActivateAndFocusSearchBox (Activate() + AttachThreadInput),
    // not a plain SearchTextBox.Focus() -- once another process's window has taken OS-level foreground,
    // WPF's own focus calls alone can't pull real keyboard input back without also re-activating this
    // window.
    private void ScheduleFocusReclaim(int delayMs)
    {
        if (_reclaimFocusTimer == null)
        {
            _reclaimFocusTimer = new DispatcherTimer();
            _reclaimFocusTimer.Tick += (s, e) =>
            {
                _reclaimFocusTimer!.Stop();
                if (_window.IsVisible) _window.ActivateAndFocusSearchBox();
            };
        }
        _reclaimFocusTimer.Stop();
        _reclaimFocusTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _reclaimFocusTimer.Start();
    }

    public void SuppressExplorerSelectionSyncForResultRefresh()
    {
        _suppressExplorerSelectionSync = true;

        // ReconcileTo fires one CollectionChanged per differing row (Replace/Add/Remove), not one Reset
        // -- every one of those reaches this method. Without a coalescing guard, per-keystroke timing
        // diagnostics showed a single result-set update queuing 20-40+ of these ContextIdle callbacks
        // back to back, each one redundantly re-running the auto-select-first-result loop AND a real IPC
        // SendMessage to the Hook process in SyncExplorerSelection below -- individually cheap
        // (sub-millisecond each) but the sheer count of queued dispatcher operations was the actual
        // source of the typing lag, not any single expensive call.
        if (Interlocked.Exchange(ref _selectionSyncQueued, 1) == 1)
            return;

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _selectionSyncQueued, 0);
            _suppressExplorerSelectionSync = false;

            // Auto-select the first valid result if nothing is selected or if user has not navigated yet
            if (!_userNavigatedSinceLastQuery || _window.LstResults.SelectedIndex < 0 || _window.LstResults.SelectedItem == null)
            {
                for (var i = 0; i < _window.LstResults.Items.Count; i++)
                {
                    if (_window.LstResults.Items[i] is AppSearchResult item
                        && !item.IsEmptyResult && !item.IsSearchSectionHeader)
                    {
                        _window.LstResults.SelectedIndex = i;
                        _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
                        break;
                    }
                }
            }

            SyncExplorerSelection();
        }), DispatcherPriority.ContextIdle);
    }

    private void MoveResultSelection(int direction)
    {
        // Wraps like the actions list's NavigateActionsList (ShellMenuPresenter.cs) -- past the last
        // item goes back to the first, and vice versa.
        _userNavigatedSinceLastQuery = true;
        var count = _window.LstResults.Items.Count;
        if (count == 0) return;
        var next = ListSelectionNavigator.NextSelectable(_window.LstResults.SelectedIndex, direction, count,
            i => _window.LstResults.Items[i] is AppSearchResult item && !item.IsEmptyResult && !item.IsSearchSectionHeader);
        if (next < 0) return;

        _window.LstResults.SelectedIndex = next;
        _window.LstResults.ScrollIntoView(_window.LstResults.SelectedItem);
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
