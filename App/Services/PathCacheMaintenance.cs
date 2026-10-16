using Lertaro.Core;

using Lertaro.Core.Services.Network;
using Lertaro.Core.Services.Search;
namespace Lertaro.App.Services;

// Gives back memory held by long-lived search caches across every live index, on both sides of the
// process boundary: local drives run in the elevated Service process and are reached by a fire-and-forget
// pipe command, network/WSL/folder-index drives run in-process.
// Most of what this used to reclaim is gone: IndexV2's Snapshot rebuilds a row's path from cheap mmap
// reads on every GetFullPath, so neither side keeps a per-row path memo any more (both legs are no-ops --
// see UsnIndexer.ClearAllPathCaches and NetworkIndex.ClearPathCache). The entry point stays because a
// search window closing/hiding is a natural point to hand memory back anyway -- called from the same
// spots that already call ShellIconHelper.ClearCache()/Win32Api.TrimWorkingSet() on close/hide -- and so
// its callers don't have to know which legs still do anything.
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
