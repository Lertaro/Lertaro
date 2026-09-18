using Lertaro.App.Services.Favorites;
using Lertaro.Core;

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
        var alreadyReported = UnavailableCombinations(owner);

        var registrations = FavoriteHotkeyRegistrations.Build(owner.Settings.Favorites);

        var failures = new List<FavoriteHotkeyFailure>();
        FavoriteHotkeyService.Instance?.Refresh(registrations, collected => failures.AddRange(collected));

        ClearHints(owner);
        ReportFailures(owner, failures, alreadyReported);
        ReportDuplicates(owner, registrations);
    }

    /// <summary>Drops every row's hotkey hint, e.g. when the page is re-opened or before reporting anew.</summary>
    public static void ClearHints(FavoritesSettingsViewModel owner)
    {
        foreach (var item in owner.Items) item.HotkeyHint = string.Empty;
    }

    // The combinations the user has already been told about. A combination another application owns
    // fails on every later Apply too, and re-announcing it each time would make an unrelated, successful
    // edit look like it failed -- so only a newly unavailable one is worth showing.
    private static HashSet<string> UnavailableCombinations(FavoritesSettingsViewModel owner)
    {
        var service = FavoriteHotkeyService.Instance;
        return owner.Items
            .Select(item => item.Hotkey)
            .Where(hotkey => service?.IsNewlyUnavailable(hotkey) == true)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void ReportFailures(
        FavoritesSettingsViewModel owner,
        IReadOnlyList<FavoriteHotkeyFailure> failures,
        HashSet<string> alreadyReported)
    {
        foreach (var failure in failures)
        {
            if (failure.OwnerIndex < 0 || failure.OwnerIndex >= owner.Items.Count) continue;
            if (alreadyReported.Contains(failure.Hotkey)) continue;

            owner.Items[failure.OwnerIndex].HotkeyHint = string.Format(
                Translation("Favorites_HotkeyUnavailable"), failure.Hotkey);
        }
    }

    // A duplicate registers nothing, so it never reaches the failures above -- without this the row
    // would look configured while a different favorite is the one that actually fires.
    private static void ReportDuplicates(
        FavoritesSettingsViewModel owner,
        IReadOnlyList<FavoriteHotkeyRegistration> registrations)
    {
        foreach (var registration in registrations)
        {
            if (registration.SkipReason != FavoriteHotkeySkipReason.Duplicate) continue;
            if (registration.OwnerIndex < 0 || registration.OwnerIndex >= owner.Items.Count) continue;

            owner.Items[registration.OwnerIndex].HotkeyHint = string.Format(
                Translation("Favorites_HotkeyInUse"), registration.Hotkey);
        }
    }

    private static string Translation(string key) => Services.TranslationManager.Instance[key];
}
