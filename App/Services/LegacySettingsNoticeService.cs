using Lertaro.App.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Tray;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
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
    /// after the window is up, so the balloon has a tray icon to attach to.
    /// </summary>
    /// <remarks>
    /// Three different legacies, one balloon: at most one of them is worth interrupting startup for, and they
    /// are checked in the order of how much the user needs to know.
    ///
    /// Every check MUTATES the settings (that is what makes each one unrepeatable), so the save below is not
    /// optional: without it the same balloon comes back on the next launch.
    /// </remarks>
    public static void RunOnStartup() => _ = Task.Run(async () =>
    {
        try
        {
            // Same delay as the update check: the tray icon is created with the quick window, and a balloon
            // shown before that has nothing to render in.
            await Task.Delay(4000);

            var settings = UserSettings.Load();
            var (title, text, changed) = Describe(settings);
            if (title == null)
                return;

            // Saved before the balloon is shown. The recorded values are one-time pieces of guidance, and a
            // failure between here and the balloon must not turn them into a once-per-launch nag.
            if (changed)
                settings.Save();

            // A balloon is the right surface: this is a notice about a saved setting, not a question, and it
            // must not block startup. Clicking it jumps straight to the prefix field -- the same entry the
            // settings search box resolves that key to.
            Application.Current?.Dispatcher.BeginInvoke(new Action(
                () => TrayIconService.Instance?.ShowBalloonTip(
                    title,
                    text!,
                    ToolTipIcon.Warning,
                    onClick: OpenTokenPrefixSetting)));
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

        var precisionItems = TakePrecisionTriggerCollisions(settings);
        if (precisionItems.Count > 0)
        {
            return (
                TranslationManager.Instance["General_PrecisionTriggerTitle"],
                string.Format(TranslationManager.Instance["General_PrecisionTriggerNotice"], string.Join(", ", precisionItems)),
                true);
        }

        if (!LegacySettingsAdvisor.ShouldShowNotice(settings))
            return (null, null, false);

        settings.LegacyTokenPrefixNoticeShown = true;
        return (
            TranslationManager.Instance["General_LegacyTokenPrefixTitle"],
            TranslationManager.Instance["General_LegacyTokenPrefixNotice"],
            true);
    }

    // The '?' collisions, as the human-readable list the notice interpolates: every entry names the value that
    // was there and, where one exists, the localized label of the setting it lived in. Interpolating labels
    // rather than writing names here is what keeps this sentence true in all seven languages.
    private static List<string> TakePrecisionTriggerCollisions(UserSettings settings)
    {
        var items = new List<string>();

        if (LegacySettingsAdvisor.TakePrecisionTriggerTokenPrefix(settings))
            items.Add($"? ({TranslationManager.Instance["General_GlobalTokenPrefix"]})");

        foreach (var (typeId, trigger) in LegacySettingsAdvisor.TakePrecisionTriggerResultTypes(settings))
            items.Add($"{trigger} ({SearchResultTypePriority.GetDisplayName(typeId) ?? typeId})");

        foreach (var (_, pluginName, _, value) in PluginTriggerKeywordMigration.TakeUnusable(settings, PluginTriggerKeywordMigration.Candidates()))
            items.Add($"{value} ({pluginName})");

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
