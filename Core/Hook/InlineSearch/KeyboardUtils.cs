namespace Lertaro.Core.Hook.InlineSearch;

internal static class KeyboardUtils
{
    public static int GetKeyVirtualCode(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;
        key = key.Trim().ToUpperInvariant();
        if (key == "SPACE") return 0x20;
        if (key == "TAB") return 0x09;
        if (key == "ENTER" || key == "RETURN") return 0x0D;
        if (key == "ESC" || key == "ESCAPE") return 0x1B;
        if (key == "BACK" || key == "BACKSPACE") return 0x08;
        if (key == "CAPSLOCK") return 0x14;

        // OEM/punctuation keys -- WPF's Key enum names these "OemN"/"OemXxx" (what HotkeyRecorderControl
        // records via Key.ToString()), and none of them were mapped here, so a hotkey using one (e.g.
        // Alt+Oem3, the `~ key) silently never matched: GetKeyVirtualCode returned 0, and the caller's
        // `targetVk != 0` guard rejected it before the key comparison ever ran.
        if (key == "OEM1") return 0xBA;      // ;:
        if (key == "OEMPLUS") return 0xBB;   // =+
        if (key == "OEMCOMMA") return 0xBC;  // ,<
        if (key == "OEMMINUS") return 0xBD;  // -_
        if (key == "OEMPERIOD") return 0xBE; // .>
        if (key == "OEM2") return 0xBF;      // /?
        if (key == "OEM3") return 0xC0;      // `~
        if (key == "OEM4") return 0xDB;      // [{
        if (key == "OEM5") return 0xDC;      // \|
        if (key == "OEM6") return 0xDD;      // ]}
        if (key == "OEM7") return 0xDE;      // '"

        // Navigation/editing keys -- same gap as the OEM keys above (see GitHub issue #153: "Alt+Home"
        // configured as the toggle-window hotkey silently never matched, since Home had no mapping here
        // and GetKeyVirtualCode returned 0).
        if (key == "HOME") return 0x24;
        if (key == "END") return 0x23;
        if (key == "PAGEUP" || key == "PRIOR") return 0x21;
        if (key == "PAGEDOWN" || key == "NEXT") return 0x22;
        if (key == "INSERT") return 0x2D;
        if (key == "DELETE") return 0x2E;
        if (key == "LEFT") return 0x25;
        if (key == "UP") return 0x26;
        if (key == "RIGHT") return 0x27;
        if (key == "DOWN") return 0x28;

        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            return key[0];
        if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
            return key[0];

        if (key.StartsWith("F") && key.Length > 1 && int.TryParse(key.Substring(1), out var fNum) && fNum >= 1 && fNum <= 12)
        {
            return 0x6F + fNum; // F1 is 0x70, F12 is 0x7B
        }

        return 0;
    }

    public static bool CheckModifiersMatch(string expectedModifier, bool trackedWindowsKeyDown = false)
    {
        var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        var shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
        var winDown = trackedWindowsKeyDown || (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                       (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        return ModifiersMatch(expectedModifier, ctrlDown, altDown, shiftDown, winDown, "NONE");
    }

    public static bool CheckModifiersMatchOnly(string expected, bool trackedWindowsKeyDown = false)
    {
        var ctrlDown = (KeyboardNativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        var altDown = (KeyboardNativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        var shiftDown = (KeyboardNativeMethods.GetKeyState(0x10) & 0x8000) != 0;
        var winDown = trackedWindowsKeyDown || (KeyboardNativeMethods.GetKeyState(0x5B) & 0x8000) != 0 ||
                       (KeyboardNativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        return ModifiersMatch(expected, ctrlDown, altDown, shiftDown, winDown, "CONTROL");
    }

    private static bool ModifiersMatch(string? expected, bool ctrlDown, bool altDown, bool shiftDown, bool winDown, string defaultModifier)
    {
        var modifiers = (expected ?? defaultModifier).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expectsCtrl = modifiers.Any(modifier => modifier.Equals("Control", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase));
        var expectsAlt = modifiers.Any(modifier => modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase));
        var expectsShift = modifiers.Any(modifier => modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase));
        var expectsWin = modifiers.Any(modifier => modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase));
        return ctrlDown == expectsCtrl && altDown == expectsAlt && shiftDown == expectsShift && winDown == expectsWin;
    }

    public static bool IsModifierKey(int vkCode, string modifier)
    {
        modifier = modifier?.Trim().ToUpperInvariant() ?? "CONTROL";
        if (modifier == "CONTROL" || modifier == "CTRL")
            return vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3;
        if (modifier == "ALT")
            return vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5;
        if (modifier == "SHIFT")
            return vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1;
        if (modifier == "WIN" || modifier == "WINDOWS")
            return vkCode == 0x5B || vkCode == 0x5C;
        return false;
    }

    // True when the foreground window's IME is actively composing (open AND in native conversion mode),
    // for any IME language. Open status alone is unreliable: TSF IMEs (MS Pinyin, Rime, ...) report open
    // even in English mode, so their open status only means "IME on", not "composing". The conversion
    // mode's CMODE_NATIVE bit (set for Chinese/kana input, cleared for English/alphanumeric) is what
    // actually distinguishes it. Runs inside the low-level keyboard hook, so both queries use
    // SendMessageTimeout with a small timeout to never stall the callback.
    public static bool IsImeActive(IntPtr fgHwnd)
    {
        if (fgHwnd == IntPtr.Zero) return false;
        var hImeWnd = KeyboardNativeMethods.ImmGetDefaultIMEWnd(fgHwnd);
        if (hImeWnd == IntPtr.Zero) return false;

        // Must be open (IME turned on).
        if (KeyboardNativeMethods.SendMessageTimeout(hImeWnd, KeyboardNativeMethods.WM_IME_CONTROL,
                (IntPtr)KeyboardNativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero,
                KeyboardNativeMethods.SMTO_ABORTIFHUNG, 40, out var open) == IntPtr.Zero || open == IntPtr.Zero)
            return false;

        // ...and in native (Chinese/kana) conversion mode. English/alphanumeric mode clears CMODE_NATIVE.
        // If the mode can't be read, fall back to the (conservative) open-status result.
        if (KeyboardNativeMethods.SendMessageTimeout(hImeWnd, KeyboardNativeMethods.WM_IME_CONTROL,
                (IntPtr)KeyboardNativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero,
                KeyboardNativeMethods.SMTO_ABORTIFHUNG, 40, out var conv) == IntPtr.Zero)
            return true;
        return (conv.ToInt64() & KeyboardNativeMethods.IME_CMODE_NATIVE) != 0;
    }

    public static char GetUnicodeChar(KeyboardNativeMethods.KBDLLHOOKSTRUCT hookStruct)
    {
        var keyboardState = new byte[256];
        KeyboardNativeMethods.GetKeyboardState(keyboardState);
        var sb = new System.Text.StringBuilder(2);
        var result = KeyboardNativeMethods.ToUnicode(hookStruct.vkCode, hookStruct.scanCode, keyboardState, sb, sb.Capacity, 0);
        if (result == 1 && !char.IsControl(sb[0]))
        {
            return sb[0];
        }
        return '\0';
    }
}
