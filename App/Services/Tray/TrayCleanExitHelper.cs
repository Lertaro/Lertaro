using Lertaro.Core;
using Lertaro.Core.Services.Search;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

internal static class TrayCleanExitHelper
{
    private static int _exiting;

    public static async void CleanExit()
    {
        if (Interlocked.Exchange(ref _exiting, 1) != 0) return;
        try
        {
            try
            {
                if (App.HookClient is { } hook) await hook.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { Logger.Log($"[TrayCleanExitHelper] Hook shutdown failed: {ex.Message}", LogLevel.Warn); }
            using var service = new SearchService();
            if (!await service.StopServiceAsync().ConfigureAwait(false))
                Logger.Log("[TrayCleanExitHelper] Service shutdown could not be confirmed.", LogLevel.Error);
        }
        catch (Exception ex)
        {
            Logger.Log($"[TrayCleanExitHelper] Shutdown failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            // Update completion also enters here from a worker thread.
            await Application.Current.Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        }
    }
}
