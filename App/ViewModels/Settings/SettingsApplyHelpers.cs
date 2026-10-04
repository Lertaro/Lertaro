using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

// Comparison helpers used only by SettingsViewModel.Apply() -- split out to keep that file under the
// line-count limit.
internal static class SettingsApplyHelpers
{
    /// <summary>
    /// Re-registers the favorites' global hotkeys now that they have been written to settings, and copies
    /// the outcome back onto the rows. Lives here rather than on the view model for the same reason as
    /// the helpers below: it keeps SettingsViewModel.Apply readable and that file under the limit.
    /// </summary>
    public static void RebindFavoriteHotkeys(FavoritesSettingsViewModel favorites) =>
        FavoriteHotkeySettingsSupport.ApplyHotkeys(favorites);

    public static bool NetworkSettingsChanged(IReadOnlyList<NetworkDriveSetting> oldSettings, IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    public static bool WslSettingsChanged(IReadOnlyList<WslSetting> oldSettings, IReadOnlyList<WslSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    public static bool FolderIndexesChanged(IReadOnlyList<FolderIndexSetting> oldSettings, IReadOnlyList<FolderIndexSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Path}|{f.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Path}|{f.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }
}
