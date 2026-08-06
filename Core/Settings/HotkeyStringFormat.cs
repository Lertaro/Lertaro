namespace Lertaro.Core;

/// <summary>
/// Shared parsing for the flat hotkey string format used by <see cref="HotkeyPageSettings.ToggleWindowHotkey"/>
/// and <see cref="HotkeyPageSettings.QuickSwitchHotkey"/>: a bare modifier token ("Ctrl"/"Alt"/"Shift"/"Win")
/// means double-tap that modifier; anything else ("Mod+Key" or a bare key) is a literal key combo.
/// </summary>
public static class HotkeyStringFormat
{
    private static readonly string[] ModifierTokens = { "Ctrl", "Alt", "Shift", "Win" };

    /// <summary>
    /// Returns whether a recorder-format hotkey is assigned to Windows itself. These combinations are
    /// intentionally unavailable to Lertaro so Windows keeps its documented behavior.
    /// Microsoft Windows 10/11 shortcuts as of 2026-07-27:
    /// Win; Win+A; Win+Shift+A; Win+Alt+B; Win+C; Win+Alt+D; Win+Alt+Down; Win+Alt+H;
    /// Win+Alt+Enter; Win+Alt+K; Win+Alt+Up; Win+Comma; Win+Ctrl+C; Win+Ctrl+D; Win+Ctrl+Enter; Win+Ctrl+F;
    /// Win+Ctrl+F4; Win+Ctrl+Left; Win+Ctrl+Q; Win+Ctrl+Right;
    /// Win+Ctrl+Shift+B; Win+Ctrl+Space; Win+Ctrl+V; Win+D; Win+Down; Win+E; Win+Esc; Win+F;
    /// Win+Slash; Win+G; Win+H; Win+Home; Win+I; Win+J; Win+K; Win+L; Win+Left; Win+M;
    /// Win+Minus; Win+N; Win+O; Win+P; Win+Pause; Win+Period; Win+Semicolon; Win+Plus;
    /// Win+PrintScreen; Win+Q; Win+R; Win+Right; Win+S; Win+Shift+Down; Win+Shift+Enter;
    /// Win+Shift+Left; Win+Shift+M; Win+Shift+R; Win+Shift+Right; Win+Shift+S;
    /// Win+Shift+Space; Win+Shift+T; Win+Shift+Up; Win+Shift+V; Win+Space; Win+Tab; Win+T;
    /// Win+U; Win+Up; Win+V; Win+W; Win+X; Win+Y; Win+Z; Win+(0-9); Win+Alt+(0-9);
    /// Win+Ctrl+(0-9); Win+Ctrl+Shift+(0-9); Win+Shift+(0-9).
    /// Source: https://support.microsoft.com/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec
    /// </summary>
    public static bool IsReservedWindowsShortcut(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parts.Contains("Win", StringComparer.OrdinalIgnoreCase) &&
            !parts.Contains("Windows", StringComparer.OrdinalIgnoreCase))
            return false;

        var modifiers = parts.Where(IsModifierToken).Select(NormalizeModifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var key = parts.LastOrDefault(part => !IsModifierToken(part));
        if (key == null) return true;

        var normalizedKey = NormalizeWindowsKey(key);
        return (normalizedKey, HasOnlyModifiers(modifiers, "Win")) switch
        {
            ("A" or "C" or "D" or "E" or "ESC" or "F" or "G" or "H" or "HOME" or "I" or "J" or "K" or "L" or "LEFT" or "M" or "MINUS" or "N" or "O" or "P" or "PAUSE" or "PERIOD" or "SEMICOLON" or "PLUS" or "PRINTSCREEN" or "Q" or "R" or "RIGHT" or "S" or "SPACE" or "TAB" or "T" or "U" or "UP" or "V" or "W" or "X" or "Y" or "Z" or "COMMA" or "SLASH", true) => true,
            ("A" or "DOWN" or "ENTER" or "LEFT" or "M" or "R" or "RIGHT" or "S" or "SPACE" or "T" or "UP" or "V", false) when HasOnlyModifiers(modifiers, "Win", "Shift") => true,
            ("B" or "D" or "DOWN" or "ENTER" or "H" or "K" or "UP", false) when HasOnlyModifiers(modifiers, "Win", "Alt") => true,
            ("C" or "D" or "ENTER" or "F" or "F4" or "LEFT" or "Q" or "RIGHT" or "SPACE" or "V", false) when HasOnlyModifiers(modifiers, "Win", "Ctrl") => true,
            ("B", false) when HasOnlyModifiers(modifiers, "Win", "Ctrl", "Shift") => true,
            _ when normalizedKey.Length == 1 && char.IsDigit(normalizedKey[0]) =>
                HasOnlyModifiers(modifiers, "Win") ||
                HasOnlyModifiers(modifiers, "Win", "Alt") ||
                HasOnlyModifiers(modifiers, "Win", "Ctrl") ||
                HasOnlyModifiers(modifiers, "Win", "Ctrl", "Shift") ||
                HasOnlyModifiers(modifiers, "Win", "Shift"),
            _ => false,
        };
    }

    private static bool IsModifierToken(string token) => ModifierTokens.Contains(token, StringComparer.OrdinalIgnoreCase) ||
        token.Equals("Control", StringComparison.OrdinalIgnoreCase) || token.Equals("Windows", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModifier(string token) => token.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" :
        token.Equals("Windows", StringComparison.OrdinalIgnoreCase) ? "Win" : token;

    private static bool HasOnlyModifiers(HashSet<string> modifiers, params string[] expected) =>
        modifiers.SetEquals(expected);

    private static string NormalizeWindowsKey(string key) => key.Trim().ToUpperInvariant() switch
    {
        "ESCAPE" => "ESC",
        "RETURN" => "ENTER",
        "OEMCOMMA" => "COMMA",
        "OEM2" => "SLASH",
        "OEMMINUS" => "MINUS",
        "OEMPERIOD" => "PERIOD",
        "OEM1" => "SEMICOLON",
        "OEMPLUS" or "ADD" => "PLUS",
        "PRINT" or "SNAPSHOT" => "PRINTSCREEN",
        _ => key.Trim().ToUpperInvariant(),
    };

    /// <summary>True if the value is a bare modifier (double-tap mode); <paramref name="modifier"/> is its
    /// canonical name ("Control"/"Alt"/"Shift"/"Win").</summary>
    public static bool IsBareModifier(string? value, out string modifier)
    {
        modifier = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !ModifierTokens.Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;

        modifier = value.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : value;
        return true;
    }

    public static void ParseCombo(string? value, out string modifier, out string key)
    {
        if (string.IsNullOrWhiteSpace(value)) { modifier = string.Empty; key = string.Empty; return; }

        var parts = value.Split('+');
        if (parts.Length == 1)
        {
            // A single token is either a bare modifier alone (e.g. "Ctrl") or a bare key with no
            // modifier (e.g. "P") -- tell them apart instead of always assuming the latter.
            if (ModifierTokens.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                modifier = parts[0].Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : parts[0];
                key = string.Empty;
            }
            else
            {
                modifier = string.Empty;
                key = parts[0];
            }
            return;
        }

        key = parts[^1];
        modifier = string.Join("+", parts[..^1].Select(part =>
            part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? "Control" : part));
    }

    private static readonly Dictionary<string, string> OemDisplaySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Oem1"] = ";",
        ["OemPlus"] = "=",
        ["OemComma"] = ",",
        ["OemMinus"] = "-",
        ["OemPeriod"] = ".",
        ["Oem2"] = "/",
        ["Oem3"] = "`",
        ["Oem4"] = "[",
        ["Oem5"] = "\\",
        ["Oem6"] = "]",
        ["Oem7"] = "'",
    };

    public static string ToDisplayText(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var parts = value.Split('+');
        if (OemDisplaySymbols.TryGetValue(parts[^1], out var symbol))
            parts[^1] = symbol;
        return string.Join("+", parts);
    }
}
