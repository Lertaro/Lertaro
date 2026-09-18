using Lertaro.Core;

namespace Lertaro.App.Services.Favorites;

/// <summary>Why a favorite produced no registration request, for the row-level hint in Settings.</summary>
public enum FavoriteHotkeySkipReason
{
    None,
    Empty,
    Invalid,
    Duplicate
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
    public static IReadOnlyList<FavoriteHotkeyRegistration> Build(IEnumerable<FavoriteItemSetting>? favorites)
    {
        var result = new List<FavoriteHotkeyRegistration>();
        if (favorites == null) return result;

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var favorite in favorites)
        {
            result.Add(BuildOne(favorite, index, claimed));
            index++;
        }

        return result;
    }

    private static FavoriteHotkeyRegistration BuildOne(FavoriteItemSetting favorite, int index, HashSet<string> claimed)
    {
        var hotkey = favorite.Hotkey?.Trim() ?? string.Empty;
        if (hotkey.Length == 0)
            return new FavoriteHotkeyRegistration(index, string.Empty, 0, 0, FavoriteHotkeySkipReason.Empty);

        if (!FavoriteHotkeyFormat.TryBuild(hotkey, out var virtualKey, out var modifiers))
            return new FavoriteHotkeyRegistration(index, hotkey, 0, 0, FavoriteHotkeySkipReason.Invalid);

        // The keep-first rule is applied on the raw (trimmed) text, so "Ctrl+D" and "Ctrl+d" -- the same
        // combination to Windows -- cannot both be registered.
        if (!claimed.Add(hotkey))
            return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.Duplicate);

        return new FavoriteHotkeyRegistration(index, hotkey, virtualKey, modifiers, FavoriteHotkeySkipReason.None);
    }
}
