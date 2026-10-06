using Lertaro.Core;
using System.IO;

using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

internal static class AppStartupServiceBootstrapper
{
    public static async Task<bool> PrepareUserSettingsAsync()
    {
        try
        {
            try { Core.Services.Installation.UserDataAccess.Verify(Logger.UserDataDir); return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

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
