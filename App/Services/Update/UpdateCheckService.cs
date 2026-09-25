using System.Windows;
using Lertaro.Core;
using Application = System.Windows.Application;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

using Lertaro.App.Services.Tray;
namespace Lertaro.App.Services.Update;

/// <summary>
/// Startup update-check flow: checks GitHub for a new release and, depending on settings, either downloads
/// and installs it without asking anything (auto-silent-update enabled, running as an administrator, service
/// reachable) or prompts the user to open the About page. A release that failed to install is left alone for
/// <see cref="UpdateRetryCooldown"/> rather than retried on every launch.
/// </summary>
/// <remarks>
/// Extracted out of App.OnStartup so that startup sequencing there doesn't carry this feature's own
/// background/UI flow inline.
/// </remarks>
public static class UpdateCheckService
{
    /// <summary>How long a release whose startup install failed is left alone before it is tried again.</summary>
    internal static readonly TimeSpan UpdateRetryCooldown = TimeSpan.FromHours(24);

    public static void RunOnStartupAsync() => _ = Task.Run(RunAsync);

    private static async Task RunAsync()
    {
        try
        {
            // Delay slightly to ensure app is fully initialized and main window is up
            await Task.Delay(3000);
            var settings = UserSettings.Load();
            if (!settings.AutoCheckUpdates)
                return;

            var release = await UpdateChecker.Instance.CheckForUpdatesAsync();
            if (release == null)
                return;

            var currentVersion = typeof(App).Assembly.GetName().Version;
            if (!IsNewerVersion(release.TagName, currentVersion, out _))
                return;

            if (IsInCooldown(settings, release.TagName, DateTimeOffset.UtcNow))
            {
                Logger.Log($"[App] Update to {release.TagName} is cooling down after a failed attempt.", LogLevel.Debug);
                return;
            }

            // The silent path asks for nothing. Checking the box is the consent, and a dialog reading
            // "press OK to run the silent update" is both the contradiction the user reported and the only
            // thing standing between them and the update. A package that fails signature verification still
            // says so -- that one is a security warning, not a confirmation.
            //
            // Checked before anything is downloaded because installing is the service's job now: an install
            // with no service registered at this path (a portable one, above all) could not apply an update
            // at all, and finding that out after pulling a full release zip every startup is not a
            // behaviour worth having.
            if (settings.AutoSilentUpdate && ElevationHelper.IsUserAdmin() &&
                ServiceInstallManager.IsInstalledAtCurrentPath() &&
                UpdateAssetSelector.SelectPortableZip(release.Assets, a => a.Name) is { } zipAsset)
            {
                // No dispatcher hop: the download is a long-running stream whose continuations have no
                // business on the UI thread, and CustomMessageBox already marshals the one dialog it may
                // show. StartSilentUpdateAsync only returns true once the updater is running, and the
                // updater needs these files unlocked, so leaving is the last step here.
                //
                // The progress goes to SilentUpdateState rather than to a window, because there is no
                // window at this point -- but the user can open one, and "no dialogs" is not the same
                // promise as "nothing to see".
                var downloadingFormat = TranslationManager.Instance["About_Downloading"];
                var progress = new Action<double>(p =>
                    SilentUpdateState.Report(string.Format(downloadingFormat, (int)(p * 100))));

                if (await UpdateInstaller.Instance.StartSilentUpdateAsync(zipAsset.BrowserDownloadUrl, progress))
                {
                    SilentUpdateState.Report(TranslationManager.Instance["About_Success"]);
                    TrayCleanExitHelper.CleanExit();
                }
                else
                {
                    // Nothing is running, so nothing should be claimed to be running. The reason is in the
                    // log, and the release will be offered again after the cooldown.
                    SilentUpdateState.Report(null);
                    RememberFailedAttempt(settings, release.TagName);
                }
                return;
            }

            // No silent update running: either it isn't enabled, or there is no release for this
            // architecture to install, in which case the About page is where the user can read why.
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var promptFormat = TranslationManager.Instance["About_NewVersionAvailablePrompt"];
                var prompt = string.Format(promptFormat, release.TagName);
                var title = TranslationManager.Instance["About_CheckUpdate"];
                MessageBox.Show(prompt, title, MessageBoxButton.OK, MessageBoxImage.Information);
                App.ShowSettingsWindow("About");
            }));
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Background startup update check failed: {ex.Message}", LogLevel.Warn);
        }
    }

    // A release tag ("v1.2.3") counts as newer only if it parses as a version strictly greater than
    // currentVersion -- an unparseable tag or a same-or-older one must never trigger the update prompt.
    internal static bool IsNewerVersion(string tagName, Version? currentVersion, out Version? latestVersion)
    {
        var cleanTag = tagName.TrimStart('v', 'V');
        return Version.TryParse(cleanTag, out latestVersion) && latestVersion > currentVersion;
    }

    /// <summary>
    /// Whether this release is still being left alone after a failed install. Only the release it failed on
    /// is cooled down, so a newer one published afterwards is offered straight away.
    /// </summary>
    internal static bool IsInCooldown(UserSettings settings, string tagName, DateTimeOffset now)
    {
        var ticks = settings.LastFailedUpdateUtcTicks;

        // Anything outside the range DateTimeOffset accepts is treated as no failure recorded: the settings
        // file is plain JSON a user can edit, and throwing here would park updates with nothing to show.
        if (settings.LastFailedUpdateTag != tagName || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
            return false;

        var failedAt = new DateTimeOffset(ticks, TimeSpan.Zero);

        // `now > failedAt` also handles a clock that has moved backwards since the failure (an NTP
        // correction, a dead CMOS battery): a failure recorded in the future is a stale one, not a reason
        // to wait out its cooldown.
        return now > failedAt && now - failedAt < UpdateRetryCooldown;
    }

    private static void RememberFailedAttempt(UserSettings settings, string tagName)
    {
        settings.LastFailedUpdateTag = tagName;
        settings.LastFailedUpdateUtcTicks = DateTime.UtcNow.Ticks;
        settings.Save();
    }
}
