using Lertaro.Core;
using System.IO;

using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

internal static class AppStartupServiceBootstrapper
{
    // A settings file another instance is still saving, or a folder an indexer has open, is a lock -- and
    // elevation cannot fix a lock. Waiting it out first is the difference between a start that would have
    // succeeded and one that showed a UAC prompt and re-ACLed the whole tree underneath it.
    private const int VerifyAttempts = 6;
    private static readonly TimeSpan VerifyRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// True when <paramref name="directory"/> is usable, waiting out a lock instead of escalating it.
    /// </summary>
    /// <remarks>
    /// Only an <see cref="IOException"/> is worth another look: a link, or an owner that is not this account,
    /// is what the elevated repair exists for and waiting cannot change either of those. Anything that is
    /// still unusable after the last attempt falls through to the repair exactly as one failure used to.
    /// </remarks>
    internal static async Task<bool> VerifyUserDataAsync(string directory, int attempts, TimeSpan delay)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { Core.Services.Installation.UserDataAccess.Verify(directory); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Never block the start: a check that keeps failing is the repair's cue, not a verdict.
                if (ex is UnauthorizedAccessException || attempt >= attempts) return false;
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }
    }

    public static async Task<bool> PrepareUserSettingsAsync()
    {
        try
        {
            // Before anything opens the folder: a relocation this build cannot support is refused with its own
            // reason, instead of reaching the layer that refuses links and being reported as a settings transfer
            // (see DataDirectoryResolver.DescribeRelocationRefusal).
            if (Core.Services.Installation.DataDirectoryResolver.DescribeRelocationRefusal(Logger.UserDataDir) is { } refusal)
                throw new SettingsDataRootUnsupportedException(
                    SettingsStartupFailure.DescribeUnsupportedDataRoot(Logger.UserDataDir, refusal));

            if (await VerifyUserDataAsync(Logger.UserDataDir, VerifyAttempts, VerifyRetryDelay)) return true;

            // A failed check never refuses the start on its own. It buys the repair below, and the start is
            // decided by whether the folder works afterwards -- not by what this process's token looked like
            // before it. Refusing a reduced or sandboxed launcher here would block a start that this same
            // folder can still serve, so the level is reported by the failure message and nowhere else.

            // 5.8.2–5.8.4 could lock the user's own directory. Give the current service a chance to
            // repair it before asking for elevation, and finish this before loading settings or plugins.
            if (await Task.Run(ServiceInstallManager.TryStartExistingService))
            {
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try { Core.Services.Installation.UserDataAccess.Verify(Logger.UserDataDir); return true; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    await Task.Delay(250);
                }
            }
            await Task.Run(ServiceInstallManager.RepairPermissions);
            Core.Services.Installation.UserDataAccess.Verify(Logger.UserDataDir);
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Unable to read or save settings in:\n{Logger.UserDataDir}\n\n{ex.Message}",
                "Lertaro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    public static void EnsureServiceStarted()
    {
        var settings = UserSettings.Load();
        if (settings.EnableEverythingIpc)
        {
            Everything.EverythingServiceBootstrapper.Start(new SearchService());
        }
        _ = Task.Run(async () =>
                                                      {
                                                          using var searchService = new SearchService();
                                                          try
                                                          {
                                                              if (await searchService.PingAsync().ConfigureAwait(false))
                                                              {
                                                                  // Something answers, but only a service registered at this exe may be used: one
                                                                  // installed from another folder (an older copy, or the build this one replaced)
                                                                  // answers Ping and then rejects every privileged request as "Unauthorized caller",
                                                                  // because it only trusts an App running beside itself. Reinstall rather than trust it.
                                                                  if (ServiceInstallManager.IsInstalledAtCurrentPath())
                                                                  {
                                                                      Logger.Log("[AppStartupServiceBootstrapper] Service already reachable on app startup.");
                                                                      return;
                                                                  }
                                                                  Logger.Log("[AppStartupServiceBootstrapper] A service is running from another folder; reinstalling it from this one.", LogLevel.Warn);
                                                              }
                                                          }
                                                          catch (Exception ex)
                                                          {
                                                              Logger.Log($"[AppStartupServiceBootstrapper] Service ping failed: {ex.Message}", LogLevel.Warn);
                                                          }

                                                          // Registered at this exe path but not running (stopped, or not up yet after boot):
                                                          // start it without elevation instead of an install/UAC prompt.
                                                          if (ServiceInstallManager.TryStartExistingService())
                                                          {
                                                              Logger.Log("[AppStartupServiceBootstrapper] Existing service started without elevation.");
                                                              return;
                                                          }

                                                          Logger.Log("[AppStartupServiceBootstrapper] Service unavailable on app startup. Attempting silent install/start.");
                                                          var installResult = ServiceInstallManager.SilentInstall(
                                                              onCompleted: () => Logger.Log("[AppStartupServiceBootstrapper] Silent install/start attempt completed."),
                                                              onFailed: ex => Logger.Log($"[AppStartupServiceBootstrapper] Silent install/start attempt failed: {ex.Message}", LogLevel.Warn));
                                                          if (installResult == ServiceInstallManager.SilentInstallResult.AlreadyRunning)
                                                              Logger.Log("[AppStartupServiceBootstrapper] Silent install already in flight; waiting for it.");
                                                      });
    }
}
