using Lertaro.Core;
using Lertaro.Core.Services.Everything;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services.Everything;

/// <summary>Coordinates the lifecycle of the Everything IPC emulation server within the App process.</summary>
public static class EverythingServiceBootstrapper
{
    private static EverythingIpcServer? _server;
    private static readonly object LockObj = new();

    public static void Start(SearchService searchService)
    {
        lock (LockObj)
        {
            if (_server != null) return;

            try
            {
                var dataProvider = new EverythingSearchDataProvider(searchService);
                _server = new EverythingIpcServer(dataProvider);
                var success = _server.Start();
                if (success)
                {
                    Logger.Log("[Everything] Emulation server started successfully.", LogLevel.Info);
                }
                else
                {
                    Logger.Log("[Everything] Emulation server failed to start.", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Everything] Error starting emulation server: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public static void Stop()
    {
        lock (LockObj)
        {
            if (_server == null) return;

            try
            {
                _server.Stop();
                _server.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Log($"[Everything] Error stopping emulation server: {ex.Message}", LogLevel.Warn);
            }
            finally
            {
                _server = null;
            }
        }
    }
}
