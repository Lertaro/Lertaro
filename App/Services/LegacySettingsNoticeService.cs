using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Tray;
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
    /// Two different legacies, one balloon: at most one of them is worth interrupting startup for, and the
    /// merged-prefix one wins because it is the change the user cannot see coming -- their filter rules stop
    /// working and there is no longer a field showing the prefix they were written against.
    /// </remarks>
    public static void RunOnStartup() => _ = Task.Run(async () =>
    {
        try
        {
            // Same delay as the update check: the tray icon is created with the quick window, and a balloon
            // shown before that has nothing to render in.
            await Task.Delay(4000);

            var settings = UserSettings.Load();
            var (title, text) = Describe(settings);
            if (title == null)
                return;

            // Saved before the balloon is shown. The recorded value is a one-time piece of guidance, and a
            // failure between here and the balloon must not turn it into a once-per-launch nag. (The other
            // legacy needs no flag: clearing the stale key is itself what makes it unrepeatable.)
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

    // Which legacy notice, if any, this settings file needs -- or (null, null) to say nothing.
    private static (string? Title, string? Text) Describe(UserSettings settings)
    {
        if (LegacySettingsAdvisor.ClearLegacyFilterPrefix(settings) is { } previous)
        {
            return (
                TranslationManager.Instance["General_MergedFilterPrefixTitle"],
                string.Format(
                    TranslationManager.Instance["General_MergedFilterPrefixNotice"],
                    previous,
                    settings.GlobalTokenPrefix));
        }

        if (!LegacySettingsAdvisor.ShouldShowNotice(settings))
            return (null, null);

        settings.LegacyTokenPrefixNoticeShown = true;
        return (
            TranslationManager.Instance["General_LegacyTokenPrefixTitle"],
            TranslationManager.Instance["General_LegacyTokenPrefixNotice"]);
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
