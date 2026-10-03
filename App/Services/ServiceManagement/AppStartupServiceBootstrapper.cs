using Lertaro.Core;

using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

internal static class AppStartupServiceBootstrapper
{
    public static void EnsureServiceStarted()
    {
        var settings = UserSettings.Load();
        // The service reads the walk's exclusion rules from the machine copy, never from this per-user
        // file (see MachineSettings.SyncExclusionRulesFrom), so an install that predates that mirror --
        // every install, on the launch after an upgrade -- has to be refreshed here. Doing it before the
        // service is (re)started, and not only when the settings page is applied, means the user does not
        // have to touch anything for their existing exclusions to start reaching the local drive walk.
        MachineSettings.MirrorExclusionRules(settings);
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
                                                                  Logger.Log("[AppStartupServiceBootstrapper] Service already reachable on app startup.");
                                                                  return;
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
