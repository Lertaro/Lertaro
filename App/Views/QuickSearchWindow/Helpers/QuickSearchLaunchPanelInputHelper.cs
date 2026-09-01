using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Keeps the quick launch panel's keyboard-only navigation separate from the general search key router.
// The panel is visible only while the query is empty, so these keys can be scoped without changing the
// result-list navigation used by normal searches.
internal static class QuickSearchLaunchPanelInputHelper
{
    public static bool Handle(KeyEventArgs e, Lertaro.App.QuickSearchWindow window)
    {
        if (window.ViewModel.LaunchPanelVisibility != Visibility.Visible)
            return false;

        if (e.Key is Key.Up or Key.Down)
        {
            window.ViewModel.MoveLaunchPanelSelection(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return true;
        }

        var actualKey = WpfUiHelper.GetActualKey(e);
        var hotkeys = UserSettings.Load().Hotkeys;
        if (WpfUiHelper.MatchesHotkey(hotkeys.NextItemHotkey, Keyboard.Modifiers, actualKey))
        {
            window.ViewModel.CycleLaunchSource(1);
            e.Handled = true;
            return true;
        }

        if (WpfUiHelper.MatchesHotkey(hotkeys.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
        {
            window.ViewModel.CycleLaunchSource(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Enter && window.ViewModel.SelectedLaunchPanelItem is { } item)
        {
            window.ExecuteFavorite(item);
            e.Handled = true;
            return true;
        }

        return false;
    }
}
