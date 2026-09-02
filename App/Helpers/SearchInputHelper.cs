using System.Windows;
using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Services;

using Lertaro.App.Services.AppWindow;

using Lertaro.App.Services.ShellMenu.Presenter;
namespace Lertaro.App.Helpers;

public static class SearchInputHelper
{
    public static bool IsQuickLookKey(System.Windows.Input.KeyEventArgs e)
    {
        var checkKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return WpfUiHelper.MatchesHotkey(UserSettings.Load().Hotkeys.QuickLookHotkey, Keyboard.Modifiers, checkKey);
    }

    // Shared by SearchWindow/QuickSearchWindow/InlineSearchWindow's own Right-arrow-opens-Actions check,
    // so it only fires when Right would otherwise be a no-op for the caret (not moving it or collapsing a
    // selection) instead of hijacking normal text-cursor movement while editing earlier in the query.
    public static bool IsSearchCaretAtEnd(ISearchWindow window) => window.SearchTextBox.IsKeyboardFocusWithin
        && window.SearchTextBox.SelectionLength == 0
        && window.SearchTextBox.CaretIndex >= window.SearchTextBox.Text.Length;

    public static bool HandleActionsModeKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow? window, ShellMenuPresenter? menuPresenter)
    {
        if (menuPresenter == null || !menuPresenter.IsInActionsMode)
            return false;

        // Read once up front: the custom next/previous-item hotkeys must win over the hardcoded bare-Tab
        // shortcut below whenever the user has bound one of them to Tab, otherwise a Tab-as-next-item
        // binding would silently be swallowed by "Tab enters submenu" and never reach the match further down.
        var actualKey = WpfUiHelper.GetActualKey(e);
        var settings = UserSettings.Load().Hotkeys;
        var isNextItemHotkey = WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, Keyboard.Modifiers, actualKey);
        var isPreviousItemHotkey = WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, Keyboard.Modifiers, actualKey);

        // Every bare key check below (including Escape) requires no modifiers -- otherwise it would
        // shadow a user-configurable combo hotkey that happens to share the same base key (e.g. the
        // Startup Panel's default Ctrl+Left/Right, or a plugin action's Ctrl+Enter) before it ever
        // reaches the handling further down (or falls through to the window's own hotkey dispatch).
        // Nothing is bound to a modified Escape today, but guarding it anyway costs nothing and rules
        // the whole bug class out here rather than leaving one exception someone has to remember.
        var noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        var actionSearchBox = window?.UsesFloatingActionsMenu == true ? window.ActionsSearchTextBox : window?.SearchTextBox;

        if (e.Key == Key.Escape && noModifiers)
        {
            HandleActionsEscape(window, menuPresenter);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Left && noModifiers)
        {
            menuPresenter.GoBackMenuOrExit();
            e.Handled = true;
            return true;
        }

        if ((e.Key == Key.Right && noModifiers) || (e.Key == Key.Tab && noModifiers && !isNextItemHotkey && !isPreviousItemHotkey))
        {
            menuPresenter.EnterSubMenu();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Down && noModifiers)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up && noModifiers)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        // The results list also accepts the user's configurable next/previous-item hotkeys (not just the
        // literal arrow keys above); the actions list should match so a custom binding still works once
        // the menu is open instead of silently falling through to move the hidden results-list selection.
        if (isNextItemHotkey)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }
        if (isPreviousItemHotkey)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Enter && noModifiers)
        {
            menuPresenter.ExecuteSelectedAction();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Back && noModifiers)
        {
            if (actionSearchBox != null && string.IsNullOrEmpty(actionSearchBox.Text))
            {
                menuPresenter.GoBackMenuOrExit();
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    // Keep every actions-menu Escape entry point identical, including mouse right-click and the
    // inline window's global keyboard hook. A non-empty action filter is cleared first; only the next
    // Escape-equivalent input navigates back or exits the actions menu.
    public static bool HandleActionsEscape(ISearchWindow? window, ShellMenuPresenter menuPresenter)
    {
        var actionSearchBox = window?.UsesFloatingActionsMenu == true ? window.ActionsSearchTextBox : window?.SearchTextBox;
        if (actionSearchBox != null && !string.IsNullOrEmpty(actionSearchBox.Text))
        {
            actionSearchBox.Clear();
            return true;
        }

        menuPresenter.GoBackMenuOrExit();
        return true;
    }

    /// <summary>
    /// Fires an action hotkey (e.g. Ctrl+C copy, Ctrl+Enter locate) on the selected result without
    /// opening any menu — the always-available behavior the quick window has. Only runs when a modifier
    /// is held and the actions menu is allowed for the selection, so plain typing pays no cost and
    /// suppressed rows (apps / plugin results / ...) suppress the hotkeys too. A bare key (no modifier)
    /// is exempt from that requirement when it's actually bound to something (checked against the real
    /// configured hotkeys, default or user-overridden, via HotkeyActionTrigger.HasBareKeyActionHotkey
    /// -- not hardcoded to e.g. Delete, so rebinding DeleteFileAction to a different bare key still
    /// works here) and, like the Right-arrow-opens-Actions check below, only when the caret is already
    /// at the end of the query -- otherwise the key keeps its normal text-editing meaning while editing
    /// earlier in the text. A non-empty selection is also always left to the search box, so standard
    /// text commands such as Ctrl+C, Ctrl+V, and Ctrl+X are not mistaken for result actions.
    /// </summary>
    public static bool TryActionHotkey(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        if (window.SearchTextBox.IsKeyboardFocusWithin && window.SearchTextBox.SelectionLength > 0)
            return false;

        var bareKeyBound = Keyboard.Modifiers == ModifierKeys.None && IsSearchCaretAtEnd(window) && HotkeyActionTrigger.HasBareKeyActionHotkey(e.Key);
        if ((Keyboard.Modifiers != ModifierKeys.None || bareKeyBound)
            && window.LstResults.SelectedItem is AppSearchResult selectedResult
            && menuPresenter != null
            && menuPresenter.CanShowActionsMenu(new[] { selectedResult }))
        {
            if (HotkeyActionTrigger.TryExecute(e, selectedResult, window))
            {
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    public static bool HandleCommonSearchKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        // 1. Actions Mode keys
        if (HandleActionsModeKeys(e, window, menuPresenter))
            return true;

        // 1b. Action hotkeys on the selected item (Ctrl+C copy, Ctrl+Enter locate, ...).
        if (TryActionHotkey(e, window, menuPresenter))
            return true;

        // 2. QuickLook
        if (window.GetType().Name != "InlineSearchWindow" && IsQuickLookKey(e))
        {
            if (window.LstResults.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                QuickLookManager.Instance.Toggle((Window)window, result.FullPath);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }
}
