using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.Views.SearchWindow;

// Split out purely to keep SearchWindowInputHandler under the repo's per-file line limit; this helper
// owns the full window's configurable Stay Open shortcut and delegates the state change to its owner.
internal static class SearchWindowStayOpenSupport
{
    internal static void Toggle(Lertaro.App.SearchWindow window)
    {
        window.Topmost = !window.Topmost;
        window.SearchBoxControl.IsStayOpen = window.Topmost;
    }

    internal static bool TryHandle(Lertaro.App.SearchWindow window, System.Windows.Input.KeyEventArgs e)
    {
        var hotkeys = UserSettings.Load().Hotkeys;
        if (!WpfUiHelper.MatchesHotkey(hotkeys.StayOpenHotkey, Keyboard.Modifiers, WpfUiHelper.GetActualKey(e)))
            return false;

        Toggle(window);
        e.Handled = true;
        return true;
    }
}
