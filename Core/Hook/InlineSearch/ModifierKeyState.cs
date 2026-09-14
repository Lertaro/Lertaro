namespace Lertaro.Core.Hook.InlineSearch;

// Tracks modifier transitions from the low-level hook itself. GetKeyState is thread-relative and can
// still report the state before the current hook event, so it cannot be used for chord detection here.
internal sealed class ModifierKeyState
{
    private static readonly int[] TrackedModifierKeys =
    [
        0x10, 0xA0, 0xA1,
        0x11, 0xA2, 0xA3,
        0x12, 0xA4, 0xA5,
        KeyboardNativeMethods.VK_LWIN, KeyboardNativeMethods.VK_RWIN
    ];

    private readonly HashSet<int> _pressedKeys = new();

    public bool IsControlDown => _pressedKeys.Any(IsControlKey);
    public bool IsAltDown => _pressedKeys.Any(IsAltKey);
    public bool IsShiftDown => _pressedKeys.Any(IsShiftKey);
    public bool IsWindowsDown => _pressedKeys.Contains(KeyboardNativeMethods.VK_LWIN)
        || _pressedKeys.Contains(KeyboardNativeMethods.VK_RWIN);

    public bool HasControlAltOrWindowsDown => IsControlDown || IsAltDown || IsWindowsDown;

    public void OnKeyDown(int vkCode) => SetModifier(vkCode, true);

    public void OnKeyUp(int vkCode) => SetModifier(vkCode, false);

    internal void Synchronize(Func<int, bool> isKeyDown)
    {
        foreach (var vkCode in TrackedModifierKeys)
            SetModifier(vkCode, isKeyDown(vkCode));
    }

    private void SetModifier(int vkCode, bool isDown)
    {
        if (!IsModifierKey(vkCode)) return;
        if (isDown) _pressedKeys.Add(vkCode);
        else _pressedKeys.Remove(vkCode);
    }

    private static bool IsModifierKey(int vkCode) => IsControlKey(vkCode) || IsAltKey(vkCode)
        || IsShiftKey(vkCode) || vkCode is KeyboardNativeMethods.VK_LWIN or KeyboardNativeMethods.VK_RWIN;

    private static bool IsControlKey(int vkCode) => vkCode is 0x11 or 0xA2 or 0xA3;

    private static bool IsAltKey(int vkCode) => vkCode is 0x12 or 0xA4 or 0xA5;

    private static bool IsShiftKey(int vkCode) => vkCode is 0x10 or 0xA0 or 0xA1;
}
