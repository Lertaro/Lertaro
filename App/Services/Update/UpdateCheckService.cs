using System.Windows;
using Lertaro.Core;
using Application = System.Windows.Application;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

using Lertaro.App.Services.Tray;
namespace Lertaro.App.Services.Update;

/// <summary>
/// Startup update-check flow: checks GitHub for a new release and, depending on settings, either
/// silently downloads and applies it (auto-silent-update enabled + running as admin) or prompts the
/// user to open the About page. Extracted out of App.OnStartup so that startup sequencing there
/// doesn't carry this feature's own background/UI flow inline.
/// </summary>
public static class UpdateCheckService
{
    public static void RunOnStartupAsync() => _ = Task.Run(async () =>
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

                                                           var dispatcher = Application.Current.Dispatcher;

                                                           // If auto silent update is enabled and user is admin, prompt user and execute silent update
                                                           if (settings.AutoSilentUpdate && ElevationHelper.IsUserAdmin())
                                                           {
                                                               var zipAsset = UpdateAssetSelector.SelectPortableZip(release.Assets, a => a.Name);
                                                               if (zipAsset != null)
                                                               {
                                                                   _ = dispatcher.BeginInvoke(new Action(async () =>
                                                                   {
                                                                       // try/catch INSIDE the async lambda: an async-void action's
                                                                       // continuations escape the outer Task.Run's catch entirely, so a
                                                                       // failed silent-update ride would surface as an unhandled
                                                                       // dispatcher exception instead of a log line.
                                                                       try
                                                                       {
                                                                           var promptFormat = TranslationManager.Instance["About_SilentUpdatePrompt"];
                                                                           var prompt = string.Format(promptFormat, release.TagName);
                                                                           var title = TranslationManager.Instance["About_CheckUpdate"];
                                                                           MessageBox.Show(prompt, title, MessageBoxButton.OK, MessageBoxImage.Information);
                                                                           var success = await UpdateInstaller.Instance.StartSilentUpdateAsync(zipAsset.BrowserDownloadUrl);
                                                                           if (success)
                                                                           {
                                                                               TrayCleanExitHelper.CleanExit();
                                                                           }
                                                                       }
                                                                       catch (Exception ex)
                                                                       {
                                                                           Logger.Log($"[App] Silent update flow failed: {ex.Message}", LogLevel.Error);
                                                                       }
                                                                   }));
                                                                   return;
                                                               }
                                                           }

                                                           _ = dispatcher.BeginInvoke(new Action(() =>
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
                                                   });

    // A release tag ("v1.2.3") counts as newer only if it parses as a version strictly greater than
    // currentVersion -- an unparseable tag or a same-or-older one must never trigger the update prompt.
    internal static bool IsNewerVersion(string tagName, Version? currentVersion, out Version? latestVersion)
    {
        var cleanTag = tagName.TrimStart('v', 'V');
        return Version.TryParse(cleanTag, out latestVersion) && latestVersion > currentVersion;
    }
}
