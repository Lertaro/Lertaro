using System.IO;
using Lertaro.Core;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Services.Network;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.ViewModels.Search;

// Shared by every search window -- QuickSearchViewModel.EnsureServiceMonitoringActive covers both the
// Quick and Inline popups (they already reuse the same ViewModel), AppWindowManager.ShowSearchWindow
// covers the full window -- one BeginSession() call per window session-start, one shared
// SearchExecutionEngine result filter. Deliberately NOT wired into Settings' own Network Drive page,
// which already does its own per-row liveness check on a 5s timer (NetworkDriveRefreshCoordinator/
// NetworkDriveRowSyncHelper) -- that stays as-is.
//
// Exists because neither existing "is this source reachable" signal is a good fit for search: local
// drives' own signal only gets refreshed when something is actively polling GetStatusAsync (a search
// window or Settings being open), and network/WSL/folder-index sources only get rechecked on whatever
// refresh interval that source is configured with (15min/hourly/daily, or never at all if set to
// Manual). A fresh, session-scoped check sidesteps both: cheap (one check per session, not per query),
// and never more than one session stale.
internal static class SearchReachabilityGate
{
    private static long _sessionVersion;
    private static volatile HashSet<string> _unreachable = new(StringComparer.OrdinalIgnoreCase);

    // Call once per session start (a search window being shown/activated). Fires an async, UI-thread-
    // non-blocking probe; until it completes, IsResultReachable keeps using whatever the previous
    // session found -- a UNC path to a dead server can take several seconds to time out, and delaying
    // this session's very first results for that isn't worth it just to catch a source that went
    // unreachable in the gap between two sessions.
    public static void BeginSession()
    {
        var version = Interlocked.Increment(ref _sessionVersion);
        _ = ProbeAsync(version);
    }

    public static bool IsResultReachable(SearchResult result) => IsResultReachable(result.Drive, _unreachable);

    internal static bool IsResultReachable(string? drive, IReadOnlySet<string> unreachable)
        => string.IsNullOrEmpty(drive) || !unreachable.Contains(drive);

    private static async Task ProbeAsync(long version)
    {
        try
        {
            using var searchService = new SearchService();
            var unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await ProbeLocalDrivesAsync(searchService, unreachable).ConfigureAwait(false);
            ProbeNetworkSources(searchService, unreachable);

            // A newer session already started (this one raced against a fast re-open) -- its own probe
            // is either already running or about to be, so let that one's result win instead.
            if (Volatile.Read(ref _sessionVersion) != version)
                return;

            _unreachable = unreachable;
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchReachabilityGate] Reachability probe failed: {ex.Message}", LogLevel.Error);
        }
    }

    private static async Task ProbeLocalDrivesAsync(SearchService searchService, HashSet<string> unreachable)
    {
        // GetStatusAsync also happens to be what drives the Service's own 5s-throttled drive-presence
        // recheck (see SearchEngine.GetStatus) -- calling it here just means a session start is one more
        // reason for that check to run, not a second/duplicate detection mechanism.
        var status = await searchService.GetStatusAsync().ConfigureAwait(false);
        foreach (var drive in status.Drives)
        {
            // Enabled is DriveMaintenanceHelper.UpdateStatus's own "isPresent && isEnabled" -- false for
            // BOTH a physically absent drive (State == "unavailable") AND one the user has simply
            // unchecked in Local Drive settings while it's still plugged in and its LiveIndex is still
            // sitting in memory (unlike a disabled network/WSL/folder source, disabling a local drive
            // does not itself clear its runtime index -- only deleting its cache does). Checking Enabled
            // directly instead of State covers both in one go.
            if (!drive.Enabled)
                unreachable.Add(drive.Drive);
        }
    }

    // Mirrors NetworkDriveRefreshCoordinator.GatherData's own reachability primitives exactly (same
    // NetworkDriveResolver/GetWslDistros/Directory.Exists calls Settings' Network Drive page already
    // uses) -- only ever configured/indexed sources (GetCachedNetworkDrives, backed by cache files on
    // disk) are worth checking, since an unindexed source has nothing to exclude from search anyway.
    private static void ProbeNetworkSources(SearchService searchService, HashSet<string> unreachable)
    {
        var resolvedDriveLetters = new HashSet<string>(
            NetworkDriveResolver.GetNetworkDrives().Select(d => d.Letter), StringComparer.OrdinalIgnoreCase);
        var wslDistros = NetworkDriveSettingsHelper.GetWslDistros();

        foreach (var key in searchService.GetCachedNetworkDrives())
        {
            if (!IsNetworkSourceReachable(key, resolvedDriveLetters, wslDistros))
                unreachable.Add(key);
        }
    }

    // Pure classification given already-resolved system state -- split out from ProbeNetworkSources so
    // tests can exercise the drive-letter/WSL/folder branching directly without a registry read or a
    // live pipe connection (ProbeNetworkSources itself, like NetworkDriveRefreshCoordinator.GatherData,
    // is exercised only by hand -- see NetworkIndexerHelperTests' own comment on this non-injectable-
    // real-path class of hazard).
    internal static bool IsNetworkSourceReachable(string cacheKey, IReadOnlySet<string> resolvedDriveLetters, IReadOnlyList<string> wslDistros)
    {
        if (cacheKey.Length == 1)
            return resolvedDriveLetters.Contains(cacheKey);
        if (WslPath.IsPath(cacheKey))
        {
            var distroName = NetworkDriveSettingsHelper.GetWslDistroName(cacheKey);
            return wslDistros.Contains(distroName, StringComparer.OrdinalIgnoreCase);
        }
        return Directory.Exists(cacheKey);
    }
}
