using Lertaro.App.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Notifications;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Application = System.Windows.Application;

namespace Lertaro.App.Services;

/// <summary>
/// Startup notice for settings values a previous release wrote that the current search syntax can no longer
/// honor -- see <see cref="LegacySettingsAdvisor"/> for which ones and why. Split out of App.OnStartup for
/// the same reason UpdateCheckService is: startup sequencing should not carry this flow inline.
/// </summary>
public static class LegacySettingsNoticeService
{
    /// <summary>
    /// Shows the notice when the saved settings still carry a legacy value, and does nothing otherwise. Run
    /// after the window is up, so the notice does not arrive ahead of the startup windows.
    /// </summary>
    /// <remarks>
    /// Shows at most one notice per launch. Obsolete filter-prefix cleanup and the one-time notice flag
    /// are persisted; conflicting search prefixes and trigger keywords are preserved for manual editing.
    /// </remarks>
    public static void RunOnStartup() => _ = Task.Run(async () =>
    {
        try
        {
            // Same delay as the update check: the quick window and the tray icon are both created during
            // startup, and a notice that lands before they are merely competes with them.
            await Task.Delay(4000);

            var settings = UserSettings.Load();
            var (title, text, changed) = Describe(settings);
            if (title == null)
                return;

            // Saved before the notice is shown. The recorded values are one-time pieces of guidance, and a
            // failure between here and the notice must not turn them into a once-per-launch nag.
            if (changed)
                settings.Save();

            // A card is the right surface: this is a notice about a saved setting, not a question, and it
            // must not block startup. Clicking it jumps straight to the prefix field -- the same entry the
            // settings search box resolves that key to.
            Application.Current?.Dispatcher.BeginInvoke(new Action(
                () => NotificationService.Show(
                    new NotificationRequest
                    {
                        Title = title,
                        Message = text!,
                        Level = NotificationLevel.Warn,
                        OnClick = OpenTokenPrefixSetting,
                    },
                    typeof(LegacySettingsNoticeService).Assembly)));
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Legacy settings notice failed: {ex.Message}", LogLevel.Warn);
        }
    });

    // Which legacy notice, if any, this settings file needs -- or (null, null, false) to say nothing. The
    // flag says whether anything was changed and therefore needs saving.
    private static (string? Title, string? Text, bool Changed) Describe(UserSettings settings)
    {
        if (LegacySettingsAdvisor.TakeLegacyFilterPrefix(settings) is { } previous)
        {
            return (
                TranslationManager.Instance["General_MergedFilterPrefixTitle"],
                string.Format(
                    TranslationManager.Instance["General_MergedFilterPrefixNotice"],
                    previous,
                    settings.GlobalTokenPrefix),
                true);
        }

        if (settings.LegacyTokenPrefixNoticeShown)
            return (null, null, false);

        var conflicts = DescribeTriggerConflicts(settings, PluginTriggerKeywordMigration.Candidates());
        if (conflicts.Count == 0)
            return (null, null, false);

        settings.LegacyTokenPrefixNoticeShown = true;
        return (
            TranslationManager.Instance["General_LegacyTokenPrefixTitle"],
            string.Format(TranslationManager.Instance["General_SearchTriggerConflictNotice"], string.Join(", ", conflicts)),
            true);
    }

    // Report saved conflicts without changing any trigger. Only the notice flag is persisted above.
    internal static List<string> DescribeTriggerConflicts(UserSettings settings,
        IEnumerable<(string PluginId, string PluginName, PluginConfigField Field)> candidates)
    {
        var items = new List<string>();
        var prefix = QueryTokenPrefixRules.PrefixFor(settings);

        if (QueryTokenPrefixRules.GlobalPrefixConflict(settings.GlobalTokenPrefix, settings.ResultTypeTriggers.Values) != null)
            items.Add($"{settings.GlobalTokenPrefix} ({TranslationManager.Instance["General_GlobalTokenPrefix"]})");

        foreach (var (typeId, trigger) in settings.ResultTypeTriggers)
            if (!string.IsNullOrEmpty(trigger) && SearchSyntaxReserved.ValidateLeadingCharacter(trigger, prefix) != null)
                items.Add($"{trigger} ({SearchResultTypePriority.GetDisplayName(typeId) ?? typeId})");

        foreach (var (pluginId, pluginName, field) in candidates)
        {
            if (field.Validation != ConfigFieldValidation.TriggerKeyword) continue;
            var value = settings.GetPluginSetting<string?>(pluginId, field.Key, null);
            if (!string.IsNullOrWhiteSpace(value) && QueryTokenPrefixRules.TriggerKeywordConflict(value, prefix) != null)
                items.Add($"{value} ({pluginName})");
        }

        return items;
    }

    // Jumps to the prefix row itself (section, tab, and highlight) rather than just the General section,
    // so the user lands on the field the notice is about. Falls back to the plain section if the index
    // entry is ever renamed -- opening the right page beats doing nothing.
    //
    // The index has to come from JumpToEntryIndexFor, not from a position in SettingsSearchIndex.Entries:
    // JumpToEntry resolves against the list BuildAllEntries builds, which skips the conditional entries, and
    // handing it a raw position landed the user four rows further down the page instead.
    private static void OpenTokenPrefixSetting()
    {
        var index = SettingsWindowSearchExtensions.JumpToEntryIndexFor("General_GlobalTokenPrefix");
        if (index >= 0)
        {
            AppWindowManager.ShowSettingsWindowEntry(index);
            return;
        }

        App.ShowSettingsWindow("General");
    }
}
