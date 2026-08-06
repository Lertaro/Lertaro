using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using MenuItem = System.Windows.Controls.MenuItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;

// Keyboard navigation for the quick-nav right-click popup (PluginContextMenuHelper), split out to
// keep that file under the line-count limit.
internal static class PluginContextMenuKeyHandler
{
    public static void Handle(
        KeyEventArgs ev,
        Popup? popup,
        DependencyPropertyKey? isHighlightedKey,
        System.Windows.Controls.Menu rightClickMenu,
        IReadOnlyList<AppSearchResult> selection,
        IPluginSearchWindow view,
        SearchWindowType menuWindowType)
    {
        if (popup == null || !popup.IsOpen || isHighlightedKey == null) return;

        // Registered action hotkeys (Ctrl+C, Ctrl+Enter, ...) fire on the item while the flyout is
        // open too; hideOnRun closes the whole quick-nav menu afterward.
        if (Keyboard.Modifiers != ModifierKeys.None
            && HotkeyActionTrigger.TryExecute(ev, selection, view, menuWindowType, hideOnRun: true))
        {
            ev.Handled = true;
            return;
        }

        var state = PluginContextMenuBuilder.GetActiveMenuState(rightClickMenu, isHighlightedKey);
        if (state.items.Count == 0) return;

        void UpdateStateHighlight(int newIdx)
        {
            if (state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count)
                state.items[state.highlightedIndex].SetValue(isHighlightedKey, false);
            if (newIdx >= 0 && newIdx < state.items.Count)
            {
                state.items[newIdx].SetValue(isHighlightedKey, true);
                state.items[newIdx].BringIntoView();
            }
        }

        // The user's configurable next/previous-item hotkeys should move the highlight here too, not
        // just the literal arrow keys -- otherwise a custom binding silently stops working once this
        // menu (or one of its nested submenus) is open.
        var actualKey = WpfUiHelper.GetActualKey(ev);
        var hotkeys = UserSettings.Load().Hotkeys;
        var effectiveKey = ev.Key;
        if (WpfUiHelper.MatchesHotkey(hotkeys.NextItemHotkey, Keyboard.Modifiers, actualKey))
            effectiveKey = Key.Down;
        else if (WpfUiHelper.MatchesHotkey(hotkeys.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
            effectiveKey = Key.Up;

        if (effectiveKey == Key.Down)
        {
            ev.Handled = true;
            UpdateStateHighlight((state.highlightedIndex + 1) % state.items.Count);
        }
        else if (effectiveKey == Key.Up)
        {
            ev.Handled = true;
            UpdateStateHighlight((state.highlightedIndex - 1 + state.items.Count) % state.items.Count);
        }
        else if (ev.Key == Key.Right)
        {
            var activeItem = (state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count) ? state.items[state.highlightedIndex] : null;
            if (activeItem != null && activeItem.HasItems)
            {
                ev.Handled = true;
                activeItem.IsSubmenuOpen = true;
                var subItems = activeItem.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled).ToList();
                if (subItems.Count > 0) subItems[0].SetValue(isHighlightedKey, true);
            }
        }
        else if (ev.Key == Key.Left)
        {
            if (state.parent is MenuItem parentMenuItem)
            {
                ev.Handled = true;
                parentMenuItem.IsSubmenuOpen = false;
            }
        }
        else if (ev.Key == Key.Escape)
        {
            ev.Handled = true;
            popup.IsOpen = false;
        }
        else if ((ev.Key == Key.Enter || ev.Key == Key.Space) && state.highlightedIndex >= 0)
        {
            var activeItem = state.items[state.highlightedIndex];
            ev.Handled = true;
            if (activeItem.HasItems)
            {
                activeItem.IsSubmenuOpen = true;
                var subItems = activeItem.Items.OfType<MenuItem>().Where(mi => mi.IsEnabled).ToList();
                if (subItems.Count > 0) subItems[0].SetValue(isHighlightedKey, true);
            }
            else
            {
                activeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
        }
    }
}
