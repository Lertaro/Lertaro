using System.Windows.Input;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Contains the press state of certain special keys.
/// </summary>
public class SpecialKeyState
{
    public bool CtrlPressed { get; set; }
    public bool ShiftPressed { get; set; }
    public bool AltPressed { get; set; }
    public bool WinPressed { get; set; }

    public ModifierKeys ToModifierKeys()
    {
        return (CtrlPressed ? ModifierKeys.Control : ModifierKeys.None) |
               (ShiftPressed ? ModifierKeys.Shift : ModifierKeys.None) |
               (AltPressed ? ModifierKeys.Alt : ModifierKeys.None) |
               (WinPressed ? ModifierKeys.Windows : ModifierKeys.None);
    }

    public static readonly SpecialKeyState Default = new()
    {
        CtrlPressed = false,
        ShiftPressed = false,
        AltPressed = false,
        WinPressed = false
    };
}

/// <summary>
/// Context provided as a parameter when invoking a Result.Action or Result.AsyncAction.
/// </summary>
public class ActionContext
{
    public SpecialKeyState SpecialKeyState { get; set; } = SpecialKeyState.Default;
}
