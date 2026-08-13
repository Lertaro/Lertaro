using Lertaro.Core;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.App.Services;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Network;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Split out of NetworkDriveSettingsViewModel to keep that file under the line-count limit. Data gathered
// off the UI thread by GatherData, handed to ApplyGatheredData to update the ViewModel's
// ObservableCollections on the UI thread -- see NetworkDriveSettingsViewModel.RefreshNetworkDrives for why
// (issue #112: this used to run entirely synchronously on every indexer status push, and the blocking
// syscalls inside it -- WNetGetConnection, the WSL \\wsl$\<distro> probe -- made Settings tab-switching
// feel stuck while a drive was actively indexing).
internal readonly record struct NetworkDriveGatheredData(
    Dictionary<string, ResolvedNetworkDrive> ResolvedByDrive,
    List<string> VisibleDrives,
    List<string> WslDistros,
    List<string> VisibleWsl,
    Dictionary<string, NetworkDriveSetting> Configured,
    Dictionary<string, WslSetting> ConfiguredWsl,
    Dictionary<string, FolderIndexSetting> ConfiguredFolders,
    Dictionary<string, NetworkIndexStatus> Statuses);

internal static class NetworkDriveRefreshCoordinator
{
    // Pure data gathering -- touches no ObservableCollection/UI-bound state, so it's safe to run off the
    // UI thread via Task.Run. Everything it returns is then handed to ApplyGatheredData on the UI thread.
    public static NetworkDriveGatheredData GatherData(UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses, SearchService searchService)
    {
        var configured = userSettings.NetworkDrives
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var configuredWsl = userSettings.WslSettings
            .Where(w => !string.IsNullOrWhiteSpace(w.Id))
            .ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
        var configuredFolders = userSettings.FolderIndexes
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

        var statuses = (indexStatuses ?? Array.Empty<NetworkIndexStatus>())
            .ToDictionary(s => s.Drive, StringComparer.OrdinalIgnoreCase);

        var resolvedDrives = NetworkDriveResolver.GetNetworkDrives();
        var resolvedByDrive = resolvedDrives.ToDictionary(d => d.Letter, StringComparer.OrdinalIgnoreCase);
        // Fetched once and reused below for both the drive-letter and WSL views (the original synchronous
        // code called this twice, once per view).
        var cachedDrives = searchService.GetCachedNetworkDrives().ToList();
        var visibleDrives = resolvedDrives.Select(d => d.Letter)
            .Concat(cachedDrives.Where(d => d.Length == 1))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wslDistros = NetworkDriveSettingsHelper.GetWslDistros();
        // Specifically "\\wsl$\..."/"\\wsl.localhost\...", not every "\\"-prefixed cached key -- a real
        // UNC share cached via the folder-index feature ("\\server\share") must not get folded in here
        // just for sharing the same leading "\\", which would show it as a fake WSL distro (and risk a
        // name collision if a real distro happens to share the share's leaf name).
        var cachedWslDrives = cachedDrives
            .Where(WslPath.IsPath)
            .Select(NetworkDriveSettingsHelper.GetWslDistroName)
            .ToList();
        var visibleWsl = wslDistros
            .Concat(cachedWslDrives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NetworkDriveGatheredData(resolvedByDrive, visibleDrives, wslDistros, visibleWsl, configured, configuredWsl, configuredFolders, statuses);
    }

    // UI-thread half of the old synchronous RefreshNetworkDrives: everything here touches
    // ObservableCollections or other UI-bound state, so it must run on the UI thread (the caller resumes
    // here, after Task.Run(GatherData), via the captured Dispatcher SynchronizationContext).
    public static void ApplyGatheredData(
        NetworkDriveSettingsViewModel vm, SearchService searchService, Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds,
        UserSettings userSettings, IReadOnlyList<NetworkIndexStatus>? indexStatuses, bool isGlobalBusy, NetworkDriveGatheredData data)
    {
        var visibleFolders = NetworkDriveFolderHelper.GetVisibleFolders(vm, searchService, userSettings);

        // Update in place (don't Clear+rebuild) whenever the drive/WSL/folder set is unchanged. A periodic
        // status refresh rebuilding the rows would replace the item a "refresh mode" ComboBox is bound to
        // and instantly close its open dropdown -- which is why the WSL refresh mode couldn't be changed
        // once indexing started producing status. Only rebuild when a drive/distro/folder is actually
        // added or removed.
        var structureUnchanged =
            vm.NetworkDrives.Count == data.VisibleDrives.Count &&
            data.VisibleDrives.All(letter => vm.NetworkDrives.Any(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase))) &&
            vm.WslDrives.Count == data.VisibleWsl.Count &&
            data.VisibleWsl.All(name => vm.WslDrives.Any(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase))) &&
            vm.FolderIndexes.Count == visibleFolders.Count &&
            visibleFolders.All(path => vm.FolderIndexes.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));

        if (vm.HasPendingEdits || structureUnchanged)
            NetworkDriveRowSyncHelper.UpdateRowsInPlace(vm, searchService, data.VisibleDrives, data.VisibleWsl, visibleFolders, data.Statuses, data.ResolvedByDrive, data.WslDistros, data.Configured, data.ConfiguredWsl, data.ConfiguredFolders);
        else
            NetworkDriveRowSyncHelper.RebuildRows(vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds, data.VisibleDrives, data.VisibleWsl, visibleFolders, data.Statuses, data.ResolvedByDrive, data.WslDistros, data.Configured, data.ConfiguredWsl, data.ConfiguredFolders);

        // Scoped to NetworkDrives alone -- this used to require every category empty at once, so the
        // "no network drives" placeholder never showed as long as some unrelated folder or WSL distro was
        // configured, leaving the Network tab's own list looking like a headers-only blank.
        vm.IsNetworkDrivesEmpty = vm.NetworkDrives.Count == 0;
        vm.DrivesPlaceholderText = TranslationManager.Instance["Network_Placeholder"];

        // Per-category busy, so an indexing folder can't disable a network drive's row controls (or the
        // reverse) just because this used to check one indexStatuses list combined across all three.
        // isGlobalBusy (the elevated local USN service's reachability) still applies to drives/WSL as
        // before -- only folders exclude it, since folder indexing never goes through that service.
        var driveBusy = isGlobalBusy || NetworkDrivePermissionsHelper.IsCategoryBusy(pendingRowRebuilds, vm.NetworkDrives.Select(d => d.Drive), indexStatuses);
        var wslBusy = isGlobalBusy || NetworkDrivePermissionsHelper.IsCategoryBusy(pendingRowRebuilds, vm.WslDrives.Select(w => $@"\\wsl$\{w.DistroName}"), indexStatuses);
        var folderBusy = NetworkDrivePermissionsHelper.IsCategoryBusy(pendingRowRebuilds, vm.FolderIndexes.Select(f => f.Path), indexStatuses);
        vm.CanRebuildDrives = vm.NetworkDrives.Any(d => d.AppliedEnabled) && !driveBusy;
        vm.CanRebuildWsl = vm.WslDrives.Any(w => w.AppliedEnabled) && !wslBusy;
        vm.CanRebuildFolders = vm.FolderIndexes.Any(f => f.AppliedEnabled) && !folderBusy;
        vm.CanAddFolder = !folderBusy;
        NetworkDrivePermissionsHelper.UpdateRowPermissions(vm, driveBusy, wslBusy, folderBusy);
        NetworkDriveSummaryHelper.UpdateSummaries(vm, indexStatuses, driveBusy, wslBusy, folderBusy);

        vm.NotifyRefreshResultChanged();
    }
}
