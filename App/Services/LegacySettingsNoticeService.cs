using Lertaro.App.Helpers;
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
    public static void RunOnStartup() => _ = Task.Run(async () =>
    {
        try
        {
            // Same delay as the update check: the tray icon is created with the quick window, and a balloon
            // shown before that has nothing to render in.
            await Task.Delay(4000);

            var settings = UserSettings.Load();
            if (!LegacySettingsAdvisor.ShouldShowNotice(settings))
                return;

            // Recorded before the balloon is shown: the notice is a one-time piece of guidance, and a
            // failure between here and the balloon must not turn it into a once-per-launch nag.
            settings.LegacyTokenPrefixNoticeShown = true;
            settings.Save();

            var title = TranslationManager.Instance["General_LegacyTokenPrefixTitle"];
            var text = TranslationManager.Instance["General_LegacyTokenPrefixNotice"];

            // A balloon is the right surface: this is a notice about a saved setting, not a question, and it
            // must not block startup. Clicking it jumps straight to the field that needs changing -- the same
            // entry the settings search box resolves that key to.
            Application.Current?.Dispatcher.BeginInvoke(new Action(
                () => TrayIconService.Instance?.ShowBalloonTip(
                    title,
                    text,
                    ToolTipIcon.Warning,
                    onClick: OpenTokenPrefixSetting)));
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Legacy settings notice failed: {ex.Message}", LogLevel.Warn);
        }
    });

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
