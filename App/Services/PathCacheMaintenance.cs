using Lertaro.Core;

using Lertaro.Core.Services.Network;
using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

// Gives back memory held by long-lived search caches across every live index, on both sides of the
// process boundary:
//  - Local drives run in the elevated Service process, reached via a fire-and-forget pipe command. Only
//    the path memo needs clearing here -- the candidate/rank cache already has its own 3s-idle-driven
//    trim (SearchEngine.OnIdleTimerTick), so by the time a window actually closes it's normally already
//    clear.
//  - Network/WSL/folder-index drives run in-process and have no idle-timer equivalent, so both their
//    path memo AND their candidate/rank cache need clearing here.
// PathMemo already self-caps at a high backstop threshold on its own (see Core's PathQueryExtensions),
// but a search window closing/hiding is also a natural point to proactively give memory back -- called
// from the same spots that already call ShellIconHelper.ClearCache()/Win32Api.TrimWorkingSet() on close/hide.
public static class PathCacheMaintenance
{
    public static void ClearAllPathCaches()
    {
        UserNetworkDriveSearch.ClearAllCaches();
        _ = ClearLocalPathCachesAsync();
    }

    private static async Task ClearLocalPathCachesAsync()
    {
        try
        {
            using var searchService = new SearchService();
            await searchService.ClearPathCachesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PathCacheMaintenance] Failed to clear local drive path caches: {ex.Message}", LogLevel.Error);
        }
    }
}
