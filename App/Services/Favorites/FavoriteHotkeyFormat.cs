using System.Windows.Input;
using Lertaro.Core;

namespace Lertaro.App.Services.Favorites;

/// <summary>
/// Turns a favorite's stored flat hotkey string (see <see cref="HotkeyStringFormat"/>) into the two
/// values <c>RegisterHotKey</c> needs, refusing anything that could not work.
/// </summary>
/// <remarks>
/// The refusal rules are why this type exists rather than a three-line conversion at the call site:
/// each of them would otherwise show a hotkey as configured while it silently never fires, which is
/// the exact failure this feature has to avoid.
/// </remarks>
public static class FavoriteHotkeyFormat
{
    /// <summary>The virtual-key code <c>RegisterHotKey</c> accepts with no modifier at all.</summary>
    public const uint NoModifier = 0;

    // RegisterHotKey accepts 1..0xFE; 0x00 means "no key". F12 is reserved by Windows for the
    // debugger, so it never reaches an application even when registration appears to succeed.
    private const uint MinVirtualKey = 0x01;
    private const uint MaxVirtualKey = 0xFE;
    private const uint ReservedF12 = 0x7B;

    /// <summary>
    /// True when <paramref name="hotkey"/> is a combination this app can actually register as a global
    /// hotkey. <paramref name="virtualKey"/> and <paramref name="modifiers"/> are only meaningful when
    /// this returns true -- on refusal they are zero, so a caller cannot accidentally register the
    /// half-built combination.
    /// </summary>
    public static bool TryBuild(string? hotkey, out uint virtualKey, out uint modifiers)
    {
        virtualKey = 0;
        modifiers = NoModifier;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;

        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(string.IsNullOrEmpty)) return false;

        var token = parts[^1];

        // A bare modifier ("Ctrl") is the double-tap form the hook-owned hotkeys support -- and the
        // recorder's own "press a modifier and release it" gesture. There is no key for a registration
        // to fire on, and registering the modifier key itself would swallow a plain Ctrl press app-wide.
        if (HotkeyStringFormat.IsBareModifier(token, out _)) return false;

        var vk = VirtualKeyOf(token);
        if (vk is < MinVirtualKey or > MaxVirtualKey or ReservedF12) return false;

        if (!TryGetModifiers(parts[..^1], out var parsedModifiers)) return false;
        virtualKey = vk;
        modifiers = ToRegisterModifiers(parsedModifiers);
        return true;
    }

    /// <summary>
    /// The virtual-key code a recorded key name stands for, or 0 for one this does not map.
    /// </summary>
    /// <remarks>
    /// Deliberately a name-to-code table rather than a <see cref="Key"/>-based one. Two reasons, both
    /// found the hard way:
    /// <list type="bullet">
    /// <item><c>KeyInterop.VirtualKeyFromKey</c> only answers correctly once WPF's input manager has
    /// taken part in an input event, so outside a live input session it returns 0 for every key -- and
    /// it cannot express a refusal either, since an unmapped key and an unavailable one both come back
    /// as 0.</item>
    /// <item>The recorder stores WPF's member names, so the value is already text. Staying in text means
    /// the mapping is one table instead of two (the enum name and its code), and it can be asserted on
    /// directly.</item>
    /// </list>
    /// The two spellings of the punctuation keys are both accepted: the recorder writes WPF's own
    /// <c>Oem1</c>/<c>OemComma</c>, while <see cref="HotkeyStringFormat.ToDisplayText"/> shows the user
    /// <c>;</c>/<c>,</c>, and either can end up in the settings file.
    /// </remarks>
    private static uint VirtualKeyOf(string token)
    {
        var clean = token.Trim();
        // WPF's recorder writes D0..D9 for the number row, not "0".."9".
        if (clean.Length == 2 && (clean[0] is 'D' or 'd') && clean[1] is >= '0' and <= '9')
            return clean[1];
        if (clean.Length == 1)
        {
            var c = char.ToUpperInvariant(clean[0]);
            if (c is >= 'A' and <= 'Z') return c;
            if (c is >= '0' and <= '9') return c;
        }

        // F1-F11 only: see ReservedF12.
        if (clean.Length is 2 or 3 && (clean[0] is 'F' or 'f') &&
            int.TryParse(clean[1..], out var functionKey) && functionKey is >= 1 and <= 11)
            return (uint)(0x70 + functionKey - 1);

        if (clean.Length is 7 or 8 && clean.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(clean[6..], out var numPad) && numPad is >= 0 and <= 9)
            return (uint)(0x60 + numPad);

        return NamedKeys.TryGetValue(clean, out var vk) ? vk : 0;
    }

    /// <summary>The keys that are not a letter or a digit, in every spelling a stored value can use.</summary>
    private static readonly Dictionary<string, uint> NamedKeys = BuildNamedKeys();

    private static Dictionary<string, uint> BuildNamedKeys()
    {
        var keys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["Return"] = 0x0D,
            ["Enter"] = 0x0D,
            ["Tab"] = 0x09,
            ["Escape"] = 0x1B,
            ["Esc"] = 0x1B,
            ["Back"] = 0x08,
            ["Backspace"] = 0x08,
            ["Space"] = 0x20,
            ["Prior"] = 0x21,
            ["PageUp"] = 0x21,
            ["Next"] = 0x22,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["Left"] = 0x25,
            ["Up"] = 0x26,
            ["Right"] = 0x27,
            ["Down"] = 0x28,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Add"] = 0x6B,
            ["Subtract"] = 0x6D,
            ["Multiply"] = 0x6A,
            ["Divide"] = 0x6F,
        };

        // The OEM keys: WPF's member name, the spelling the user sees, and the symbol itself. All three
        // resolve to the same key, so a value that was recorded, hand-edited, or displayed and retyped
        // behaves identically.
        Add(keys, 0xBA, "Oem1", "Semicolon", ";");
        Add(keys, 0xBB, "OemPlus", "Plus", "=");
        Add(keys, 0xBC, "OemComma", "Comma", ",");
        Add(keys, 0xBD, "OemMinus", "Minus", "-");
        Add(keys, 0xBE, "OemPeriod", "Period", ".");
        Add(keys, 0xBF, "Oem2", "Slash", "/");
        Add(keys, 0xC0, "Oem3", "Tilde", "`");
        Add(keys, 0xDB, "Oem4", "OpenBracket", "[");
        Add(keys, 0xDC, "Oem5", "Backslash", "\\");
        Add(keys, 0xDD, "Oem6", "CloseBracket", "]");
        Add(keys, 0xDE, "Oem7", "Quote", "'");
        return keys;
    }

    private static void Add(Dictionary<string, uint> keys, uint virtualKey, params string[] names)
    {
        foreach (var name in names) keys[name] = virtualKey;
    }

    /// <summary>
    /// The modifier flags named by the parts of a stored combination (everything before the key).
    /// Reject unknown tokens instead of silently registering a different combination.
    /// </summary>
    private static bool TryGetModifiers(IEnumerable<string> modifierTokens, out ModifierKeys modifiers)
    {
        modifiers = ModifierKeys.None;
        foreach (var token in modifierTokens)
        {
            var modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" or "WINDOWS" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (modifier == ModifierKeys.None) return false;
            modifiers |= modifier;
        }

        return true;
    }

    /// <summary>
    /// Maps WPF modifier flags onto the <c>MOD_*</c> values <c>RegisterHotKey</c> expects. Public and
    /// separate from <see cref="TryBuild"/> so the mapping itself is asserted on its own.
    /// </summary>
    public static uint ToRegisterModifiers(ModifierKeys modifiers)
    {
        var result = NoModifier;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= FavoriteHotkeyNativeMethods.ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= FavoriteHotkeyNativeMethods.ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= FavoriteHotkeyNativeMethods.ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= FavoriteHotkeyNativeMethods.ModWin;

        // MOD_NOREPEAT: holding the combination down must not navigate the foreground window over and
        // over. Windows only honours this bit from Windows 7 on, and only when a modifier is present,
        // which every accepted value here has (see TryBuild).
        return result == NoModifier ? NoModifier : result | FavoriteHotkeyNativeMethods.ModNoRepeat;
    }
}
