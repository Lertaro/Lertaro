using Lertaro.Core;

using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

internal static class AppStartupServiceBootstrapper
{
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
                                                                  await ServeMachineSettingsAsync(searchService).ConfigureAwait(false);
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
                                                              await ServeMachineSettingsAsync(searchService).ConfigureAwait(false);
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

    /// <summary>
    /// Serves the machine settings before startup gets far enough to ask about a drive, without waiting on
    /// a service that is not there yet.
    /// </summary>
    /// <remarks>
    /// Startup reads the drive selection several times in its first quarter of a second -- the routing and
    /// the scope checks both consult it -- and each of those reads fails into defaults until the copy has
    /// been served. Fetching here, ahead of them, is what keeps those answers right from the start rather
    /// than only from the first round trip onwards.
    ///
    /// The bound matters more than the fetch: a service mid-install or stopped would otherwise hold the
    /// whole UI behind a pipe that is not listening, and the copy is worth a moment of startup, not a
    /// stall. Nothing is lost when it expires -- <see cref="EnsureServiceStarted"/> fetches again from each
    /// branch that leaves the service reachable, which also covers the service this startup had to start.
    /// </remarks>
    public static async Task ServeMachineSettingsEarlyAsync()
    {
        using var searchService = new SearchService();
        using var timeout = new CancellationTokenSource(EarlyServeTimeoutMs);
        try
        {
            // The getter serves it; nothing here needs the return value.
            _ = await searchService.GetMachineSettingsAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Including the cancelled fetch: a service that has not answered yet is the state this whole
            // method is written to tolerate.
            Logger.Log($"[AppStartupServiceBootstrapper] Machine settings not served at startup: {ex.Message}", LogLevel.Info);
        }
    }

    // Enough for a service that is listening (a pipe round trip is single-digit milliseconds) and short
    // enough that a service which is not costs the startup barely a blink.
    private const int EarlyServeTimeoutMs = 600;

    /// <summary>
    /// Takes the machine settings off the service and hands them to <see cref="MachineSettings"/>.
    /// </summary>
    /// <remarks>
    /// The App cannot read machine-settings.json itself once the service has locked <c>Data\Machine</c> for
    /// itself -- see <see cref="MachineSettings.Serve"/>, which is where the copy lands and where the reason
    /// is written out. Called from each branch above that leaves the service reachable, so the copy is in
    /// place before the first search asks the routing whether a drive is indexed.
    ///
    /// Best effort by design: a pipe that is not up yet leaves nothing served, and every reader falls back
    /// to the file and then to defaults, exactly as it did before this existed.
    /// </remarks>
    private static async Task ServeMachineSettingsAsync(SearchService searchService)
    {
        try
        {
            // The getter serves it; nothing here needs the return value.
            _ = await searchService.GetMachineSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[AppStartupServiceBootstrapper] Could not read machine settings from the service: {ex.Message}", LogLevel.Warn);
        }
    }
}
