using Lertaro.Core;

namespace Lertaro.App.Services.Favorites;

/// <summary>Why a favorite produced no registration request, for the row-level hint in Settings.</summary>
public enum FavoriteHotkeySkipReason
{
    None,
    Empty,
    Invalid,
    Duplicate,
    Reserved,
    ApplicationConflict
}

/// <summary>
/// One favorite's hotkey, ready to hand to <c>RegisterHotKey</c>. <see cref="OwnerIndex"/> is the
/// favorite's position in the list the request was built from, which is how a registration failure
/// finds its way back to the row that caused it.
/// </summary>
public sealed record FavoriteHotkeyRegistration(
    int OwnerIndex,
    string Hotkey,
    uint VirtualKey,
    uint Modifiers,
    FavoriteHotkeySkipReason SkipReason);

/// <summary>
/// Builds the registration list from the user's favorites: this is the whole "which favorites get a
/// hotkey" policy, kept pure so it can be asserted without touching Win32.
/// </summary>
public static class FavoriteHotkeyRegistrations
{
    /// <summary>
    /// One request per favorite, in favorite order. An unset or unparsable combination is dropped; a
    /// combination a later favorite repeats is dropped <em>for that later favorite</em>, so the first
    /// favorite owning a combination is the one that keeps it and no combination is ever registered
    /// twice. Dropped favorites are still returned (with their <see cref="FavoriteHotkeySkipReason"/>)
    /// so the Settings page can explain the row instead of silently ignoring it.
    /// </summary>
    public static IReadOnlyList<FavoriteHotkeyRegistration> Build(IEnumerable<FavoriteItemSetting>? favorites,
        HotkeyPageSettings? hotkeys = null, IEnumerable<string>? pluginHotkeys = null)
    {
        var result = new List<FavoriteHotkeyRegistration>();
        if (favorites == null) return result;

        var claimed = new HashSet<(uint Key, uint Modifiers)>();
        var applicationKeys = new HashSet<(uint Key, uint Modifiers)>();
        foreach (var combo in (hotkeys == null ? [] : FavoriteHotkeyConflicts.HostHotkeys(hotkeys))
                     .Concat(pluginHotkeys ?? []))
            if (FavoriteHotkeyFormat.TryBuild(combo, out var key, out var modifiers))
                applicationKeys.Add((key, modifiers));
        var index = 0;

        foreach (var favorite in favorites)
        {
            result.Add(BuildOne(favorite, index, claimed, applicationKeys));
            index++;
        }

        return result;
    }

    internal static IReadOnlyList<FavoriteHotkeyRegistration> BuildForSettings(UserSettings settings) =>
        Build(settings.Favorites, settings.Hotkeys, FavoriteHotkeyConflicts.PluginHotkeys(settings));

    private static FavoriteHotkeyRegistration BuildOne(FavoriteItemSetting favorite, int index,
        HashSet<(uint Key, uint Modifiers)> claimed, HashSet<(uint Key, uint Modifiers)> applicationKeys)
    {
        var hotkey = favorite.Hotkey?.Trim() ?? string.Empty;
        if (hotkey.Length == 0)
            return new FavoriteHotkeyRegistration(index, string.Empty, 0, 0, FavoriteHotkeySkipReason.Empty);

        if (!FavoriteHotkeyFormat.TryBuild(hotkey, out var virtualKey, out var modifiers))
            return new FavoriteHotkeyRegistration(index, hotkey, 0, 0, FavoriteHotkeySkipReason.Invalid);

        if (HotkeyStringFormat.IsReservedWindowsShortcut(hotkey)
            || FavoriteHotkeyConflicts.IsExplorerShortcut(virtualKey, modifiers))
            return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.Reserved);

        if (applicationKeys.Contains((virtualKey, modifiers)))
            return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.ApplicationConflict);

        // Compare physical combinations, including aliases and modifier order (Ctrl+Shift+1 == Shift+Control+D1).
        if (!claimed.Add((virtualKey, modifiers)))
            return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.Duplicate);

        return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.None);
    }
}
