using Lertaro.App.Services.Favorites;

namespace Lertaro.App.ViewModels.Settings;

// Split out purely to keep FavoritesSettingsViewModel under the repository's per-file line limit. This
// class has no state of its own; it always operates on the one view model that owns the rows.
//
// It is also where the per-row hotkey hint is decided, because whether a combination can be registered
// is not knowable from the row itself: only the OS can answer that, and only after the attempt.
internal static class FavoriteHotkeySettingsSupport
{
    /// <summary>
    /// Registers the favorites' hotkeys after they were written to settings, then puts the outcome on
    /// the rows. Two things cannot be seen from the field itself and are reported here: a combination
    /// Windows refused because another application already owns it, and one an earlier favorite claimed.
    /// </summary>
    public static void ApplyHotkeys(FavoritesSettingsViewModel owner)
    {
        var registrations = FavoriteHotkeyRegistrations.BuildForSettings(owner.Settings);

        var failures = new List<FavoriteHotkeyFailure>();
        FavoriteHotkeyService.Instance?.Refresh(registrations, collected => failures.AddRange(collected));

        ClearHints(owner);
        ReportFailures(owner, failures);
        ReportSkipped(owner, registrations);
    }

    /// <summary>Drops every row's hotkey hint, e.g. when the page is re-opened or before reporting anew.</summary>
    public static void ClearHints(FavoritesSettingsViewModel owner)
    {
        foreach (var item in owner.Items) item.HotkeyHint = string.Empty;
    }

    // Validate the draft without registering it. A rejected combination remains visible/editable,
    // and the hint persists across Apply and reopening Settings until the conflict is resolved.
    public static void RefreshHints(FavoritesSettingsViewModel owner)
    {
        ClearHints(owner);
        var registrations = FavoriteHotkeyRegistrations.Build(
            owner.Items.Select(item => new Core.FavoriteItemSetting { Hotkey = item.Hotkey }),
            owner.Settings.Hotkeys, FavoriteHotkeyConflicts.PluginHotkeys(owner.Settings));
        foreach (var item in owner.Items)
            if (FavoriteHotkeyService.Instance?.IsNewlyUnavailable(item.Hotkey) == true)
                item.HotkeyHint = Translation("Favorites_HotkeyUnavailable");
        ReportSkipped(owner, registrations);
    }

    private static void ReportFailures(
        FavoritesSettingsViewModel owner,
        IReadOnlyList<FavoriteHotkeyFailure> failures)
    {
        foreach (var failure in failures)
        {
            if (failure.OwnerIndex < 0 || failure.OwnerIndex >= owner.Items.Count) continue;

            owner.Items[failure.OwnerIndex].HotkeyHint = string.Format(
                Translation("Favorites_HotkeyUnavailable"), failure.Hotkey);
        }
    }

    // A duplicate registers nothing, so it never reaches the failures above -- without this the row
    // would look configured while a different favorite is the one that actually fires.
    private static void ReportSkipped(
        FavoritesSettingsViewModel owner,
        IReadOnlyList<FavoriteHotkeyRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (registration.OwnerIndex < 0 || registration.OwnerIndex >= owner.Items.Count) continue;
            var key = registration.SkipReason switch
            {
                FavoriteHotkeySkipReason.Invalid => "Favorites_HotkeyInvalid",
                FavoriteHotkeySkipReason.Duplicate => "Favorites_HotkeyInUse",
                FavoriteHotkeySkipReason.Reserved => "Favorites_HotkeyReserved",
                FavoriteHotkeySkipReason.ApplicationConflict => "Favorites_HotkeyApplicationConflict",
                _ => null
            };
            if (key != null) owner.Items[registration.OwnerIndex].HotkeyHint = Translation(key);
        }
    }

    private static string Translation(string key) => Services.TranslationManager.Instance[key];
}
