using System.Windows.Automation;

namespace Lertaro.Plugins.Files.Automation;

/// <summary>
/// Whether the currently-focused UI element looks like an editable text control (address bar, rename box,
/// filter box, ...). Checked via UI Automation's semantic ControlType rather than a native window class,
/// since Files is a WinUI3/XAML host -- its controls don't expose distinct native child window classes the
/// generic "is a text input focused" check elsewhere in the app could key off.
///
/// Throttled because this is read from CanTrigger, which runs on every keystroke inside the low-level
/// keyboard hook for as long as no inline search window is open yet -- e.g. for the whole duration of
/// typing into Files' own address bar/omnibar. An unthrottled UIA call there risks noticeable input lag,
/// or the hook being silently dropped by Windows for responding too slowly.
/// </summary>
internal static class UiaFocusTracker
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(250);
    private static DateTime _lastCheckUtc = DateTime.MinValue;
    private static bool _lastResult;

    public static bool IsFocusedElementEditable()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCheckUtc < Throttle)
            return _lastResult;

        _lastCheckUtc = now;
        try
        {
            var focused = AutomationElement.FocusedElement;
            var controlType = focused?.Current.ControlType;
            _lastResult = controlType == ControlType.Edit || controlType == ControlType.Document;
        }
        catch
        {
            _lastResult = false;
        }
        return _lastResult;
    }
}
