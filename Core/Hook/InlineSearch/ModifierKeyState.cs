namespace Lertaro.Core.Hook.InlineSearch;

// Tracks hook transitions for chords and for keys held before a bare-modifier tap begins.
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

    public void OnKeyDown(int vkCode) => _pressedKeys.Add(vkCode);

    public void OnKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
        // Async snapshots include both a side-specific key and its generic modifier alias.
        if (IsControlKey(vkCode) && !_pressedKeys.Contains(0xA2) && !_pressedKeys.Contains(0xA3)) _pressedKeys.Remove(0x11);
        if (IsAltKey(vkCode) && !_pressedKeys.Contains(0xA4) && !_pressedKeys.Contains(0xA5)) _pressedKeys.Remove(0x12);
        if (IsShiftKey(vkCode) && !_pressedKeys.Contains(0xA0) && !_pressedKeys.Contains(0xA1)) _pressedKeys.Remove(0x10);
    }

    internal bool HasOtherKeyDown(int vkCode) => _pressedKeys.Any(key => key != vkCode
        && !(key == 0x11 && IsControlKey(vkCode))
        && !(key == 0x12 && IsAltKey(vkCode))
        && !(key == 0x10 && IsShiftKey(vkCode)));

    internal void Synchronize(Func<int, bool> isKeyDown, bool includeAllKeys = false)
    {
        if (includeAllKeys)
        {
            // Only a potential modifier tap needs a complete snapshot, including mouse buttons.
            for (var vkCode = 1; vkCode < 255; vkCode++)
                SetKey(vkCode, isKeyDown(vkCode));
        }
        else
        {
            foreach (var vkCode in TrackedModifierKeys)
                SetKey(vkCode, isKeyDown(vkCode));
        }
    }

    private void SetKey(int vkCode, bool isDown)
    {
        if (isDown) _pressedKeys.Add(vkCode);
        else _pressedKeys.Remove(vkCode);
    }

    private static bool IsControlKey(int vkCode) => vkCode is 0x11 or 0xA2 or 0xA3;

    private static bool IsAltKey(int vkCode) => vkCode is 0x12 or 0xA4 or 0xA5;

    private static bool IsShiftKey(int vkCode) => vkCode is 0x10 or 0xA0 or 0xA1;
}
