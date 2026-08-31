using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Keeps the quick launch panel's keyboard-only navigation separate from the general search key router.
// The panel is visible only while the query is empty, so these keys can be scoped without changing the
// result-list navigation used by normal searches.
internal static class QuickSearchLaunchPanelInputHelper
{
    public static bool Handle(KeyEventArgs e, Lertaro.App.QuickSearchWindow window)
    {
        if (window.ViewModel.LaunchPanelVisibility != Visibility.Visible
            || Keyboard.Modifiers != ModifierKeys.None)
            return false;

        if (e.Key is Key.Up or Key.Down)
        {
            window.ViewModel.MoveLaunchPanelSelection(e.Key == Key.Down ? 1 : -1);
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
