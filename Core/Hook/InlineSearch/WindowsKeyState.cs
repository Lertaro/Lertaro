namespace Lertaro.Core.Hook.InlineSearch;

// GetKeyState can still report the pre-event state from inside a low-level keyboard-hook callback.
// Track the Windows keys from that callback so a following key in the same chord can match reliably.
internal sealed class WindowsKeyState
{
    private bool _leftDown;
    private bool _rightDown;

    public bool IsDown => _leftDown || _rightDown;

    public void OnKeyDown(int vkCode)
    {
        if (vkCode == KeyboardNativeMethods.VK_LWIN) _leftDown = true;
        if (vkCode == KeyboardNativeMethods.VK_RWIN) _rightDown = true;
    }

    public void OnKeyUp(int vkCode)
    {
        if (vkCode == KeyboardNativeMethods.VK_LWIN) _leftDown = false;
        if (vkCode == KeyboardNativeMethods.VK_RWIN) _rightDown = false;
    }
}
