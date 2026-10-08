using Lertaro.App.Helpers;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;

namespace Lertaro.App.Services.Favorites;

internal static class FavoriteHotkeyConflicts
{
    // Explorer accelerators are local to its window: RegisterHotKey can succeed for them while
    // stealing Explorer's command. Check them explicitly before attempting a global registration.
    // ponytail: documented accelerators only; localized menu mnemonics and third-party shell
    // extensions need their own rules if supported later. RegisterHotKey handles global owners.
    // Windows 10/11 File Explorer shortcuts:
    // https://support.microsoft.com/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec
    internal static bool IsExplorerShortcut(uint key, uint modifiers)
    {
        modifiers &= ~FavoriteHotkeyNativeMethods.ModNoRepeat;
        const uint ctrl = FavoriteHotkeyNativeMethods.ModControl;
        const uint alt = FavoriteHotkeyNativeMethods.ModAlt;
        const uint shift = FavoriteHotkeyNativeMethods.ModShift;
        return modifiers switch
        {
            0 => key is 0x08 or 0x09 or 0x0D or 0x1B or 0x20 or >= 0x21 and <= 0x28 or 0x2E
                or >= 0x70 and <= 0x75 or 0x79 or 0x7A or 0x6A or 0x6B or 0x6D,
            ctrl => key is 'A' or 'C' or 'D' or 'E' or 'F' or 'L' or 'N' or 'R' or 'T' or 'V' or 'W' or 'X' or 'Y' or 'Z'
                or 0x08 or 0x09 or 0x20 or >= 0x23 and <= 0x28 or 0x2D or 0x2E or 0x6B,
            ctrl | shift => key is 'C' or 'E' or 'N' or 'T' or >= '1' and <= '8'
                or 0x09 or >= 0x23 and <= 0x28,
            alt => key is 'D' or 'P' or 0x0D or 0x20 or >= 0x25 and <= 0x28 or 0x73,
            alt | shift => key == 'P',
            shift => key is 0x09 or >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x79,
            _ => false
        };
    }

    internal static IEnumerable<string> HostHotkeys(HotkeyPageSettings settings)
    {
        string[] hotkeys = [settings.ToggleWindowHotkey, settings.QuickSwitchHotkey,
            settings.QuickPanelHotkey, settings.QuickNavigationHotkey, settings.NextItemHotkey,
            settings.PreviousItemHotkey, settings.ActionsMenuHotkey, settings.CompleteFromSelectionHotkey,
            settings.QuickLookHotkey, settings.KeywordHistoryPreviousHotkey, settings.KeywordHistoryNextHotkey,
            settings.KeywordHistoryDeleteHotkey, settings.OpenFullWindowHotkey, settings.LocalSendSendWindowHotkey,
            settings.StayOpenHotkey];
        foreach (var hotkey in hotkeys) yield return hotkey;
        if (string.IsNullOrWhiteSpace(settings.SelectJumpModifier)) yield break;
        for (var number = 1; number <= 9; number++)
        {
            yield return $"{settings.SelectJumpModifier}+{number}";
            yield return $"{settings.SelectJumpModifier}+NumPad{number}";
        }
    }

    internal static IEnumerable<string> PluginHotkeys(UserSettings settings)
    {
        foreach (var registration in PluginManager.Instance.Actions)
            yield return HotkeyActionTrigger.ResolveEffectiveHotkey(registration.Action, registration.Plugin,
                settings.Hotkeys.PluginActionHotkeys);

        // Dynamic custom actions expose their hotkeys only for a concrete search selection. Read their
        // saved configuration without executing providers or fabricating a selected file.
        if (settings.DisabledPluginAssemblies.Contains("Lertaro.Plugins.CustomActions.dll", StringComparer.OrdinalIgnoreCase)
            || settings.DisabledPluginComponents.Contains(
                "Lertaro.Plugins.CustomActions.dll::DynamicActionProvider::DynamicActionProvider", StringComparer.OrdinalIgnoreCase))
            yield break;
        var actions = ConfigValueHelper.UnpackValue(settings.GetPluginSetting<object?>(
            "Lertaro.Plugins.CustomActions", "Actions", null)) as System.Collections.IEnumerable;
        if (actions == null) yield break;
        foreach (var action in actions)
            if (ConfigValueHelper.UnpackValue(action) is IDictionary<string, object> fields
                && (!fields.TryGetValue("Enabled", out var enabled) || enabled is not false)
                && fields.TryGetValue("Hotkey", out var hotkey) && hotkey is string text)
                yield return text;
    }
}
