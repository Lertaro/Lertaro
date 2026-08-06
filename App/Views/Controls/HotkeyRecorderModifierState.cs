using System.Windows.Input;

namespace Lertaro.App.Views.Controls;

internal static class HotkeyRecorderModifierState
{
    public static ModifierKeys Add(ModifierKeys modifiers, Key key) => modifiers | FromKey(key);

    public static ModifierKeys Remove(ModifierKeys modifiers, Key key) => modifiers & ~FromKey(key);

    public static ModifierKeys Combine(ModifierKeys reportedModifiers, ModifierKeys trackedModifiers) =>
        reportedModifiers | trackedModifiers;

    public static ModifierKeys FromKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None,
    };
}
