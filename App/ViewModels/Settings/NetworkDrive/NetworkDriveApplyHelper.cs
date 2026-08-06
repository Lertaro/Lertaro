using Lertaro.Core;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Network;
namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

internal static class NetworkDriveApplyHelper
{
    public static async Task ApplyChangesAsync(
        SearchService searchService,
        IReadOnlyList<NetworkDriveSetting> previousSettings,
        IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        searchService.ConfigureNetworkIndexes();

        var previousByDrive = previousSettings
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var drive in newSettings
                     .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                     .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.First())
                     .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            previousByDrive.TryGetValue(drive.Id, out var previous);
            var resolvedDrive = NetworkDriveResolver.GetNetworkDrives()
                .FirstOrDefault(d => string.Equals(NetworkDriveResolver.GetNetworkId(d.Letter), drive.Id, StringComparison.OrdinalIgnoreCase))
                ?.Letter;
            if (previous != null || string.IsNullOrWhiteSpace(resolvedDrive) || searchService.HasNetworkDriveCache(resolvedDrive))
                continue;

            // ConfigureNetworkIndexes() above already auto-queues an initial refresh for this exact drive
            // (new, no cache) -- if that's what's making the network subsystem busy, waiting on it (rather
            // than excluding it) and then unconditionally re-triggering once it clears would restart the
            // very thing a user's Stop click just interrupted, since a cancelled scan going idle looks
            // identical to one that finished. Exclude the target drive from the "is anything else busy"
            // wait, and re-check its own live status afterward -- a HasNetworkDriveCache recheck wouldn't
            // catch a Stop click before the first checkpoint (no cache file yet either way), but the
            // in-memory status already moved off "pending" the instant Configure()'s auto-queued refresh
            // started, was stopped, finished, or errored -- any of which means there's nothing left for us
            // to trigger here.
            if (await WaitForNetworkIdleAsync(searchService, resolvedDrive)
                && IsStillUntouched(searchService, resolvedDrive)
                && searchService.RefreshNetworkDriveIndex(resolvedDrive))
                await WaitForNetworkDriveRefreshAsync(searchService, resolvedDrive);
        }
    }

    private static bool IsStillUntouched(SearchService searchService, string drive)
    {
        var status = searchService.GetNetworkIndexStatuses().FirstOrDefault(s => s.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        return status == null || status.State == "pending";
    }

    private static async Task<bool> WaitForNetworkIdleAsync(SearchService searchService, string excludeDrive)
    {
        for (var i = 0; i < 120; i++)
        {
            if (!searchService.GetNetworkIndexStatuses().Any(s => s.State is "pending" or "indexing" && !s.Drive.Equals(excludeDrive, StringComparison.OrdinalIgnoreCase)))
                return true;

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task WaitForNetworkDriveRefreshAsync(SearchService searchService, string drive)
    {
        for (var i = 0; i < 120; i++)
        {
            var status = searchService.GetNetworkIndexStatuses()
                .FirstOrDefault(s => s.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (status?.State is not ("pending" or "indexing"))
                return;

            await Task.Delay(500);
        }
    }
}
