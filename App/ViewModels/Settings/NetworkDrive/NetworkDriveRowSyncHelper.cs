using System.IO;
using Lertaro.Core;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Network;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Bulk per-refresh row sync for ALL THREE row categories on the Network Drive settings page (network
// drives, WSL distros, folder indexes) -- they share the same refresh cadence and row-shape, so a single
// pass updates or rebuilds all three together. Kept separate from NetworkDriveFolderHelper, which is
// folder-only (the add-folder dialog workflow): this class has no "folder" identity of its own, it just
// happens to be the thing that also syncs folder rows alongside drive/WSL rows.
internal static class NetworkDriveRowSyncHelper
{
    /// <summary>Updates existing rows when the set of configured drives and folders has not changed.</summary>
    /// <remarks>
    /// Preserve IsEnabled: it reflects saved configuration, not current reachability. Clearing it for an
    /// offline target raises PropertyChanged, marks a pending edit, and can remove the saved entry on Apply.
    /// Use IsPresent, state text, and CanEditEnabled to report offline targets and disable their controls.
    /// </remarks>
    public static void UpdateRowsInPlace(
        NetworkDriveSettingsViewModel vm, SearchService searchService,
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var letter in visibleDrives)
        {
            var item = vm.NetworkDrives.FirstOrDefault(d => d.Drive.Equals(letter, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(letter, out var indexStatus);
            resolvedByDrive.TryGetValue(letter, out var drive);
            item.Id = NetworkDriveResolver.GetNetworkId(letter);
            item.IsPresent = drive != null;
            // IsEnabled is deliberately NOT touched here. See the note above UpdateRowsInPlace.
            vm.TrackPendingRebuild(letter, indexStatus?.State);
            item.State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(drive, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configured.TryGetValue(item.Id, out var saved);
            vm.UpdateRowAction(item, item.IsPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.Drive));
        }
        foreach (var name in visibleWsl)
        {
            var item = vm.WslDrives.FirstOrDefault(d => d.DistroName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            var unc = $@"\\wsl$\{name}";
            statuses.TryGetValue(unc, out var indexStatus);
            var isPresent = wslDistros.Contains(name, StringComparer.OrdinalIgnoreCase);
            item.IsPresent = isPresent;
            // IsEnabled is deliberately NOT touched here. See the note above UpdateRowsInPlace.
            vm.TrackPendingRebuild(unc, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredWsl.TryGetValue(name, out var saved);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.UncPath));
        }
        foreach (var path in visibleFolders)
        {
            var item = vm.FolderIndexes.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (item == null) continue;
            statuses.TryGetValue(path, out var indexStatus);
            var isPresent = Directory.Exists(path);
            item.IsPresent = isPresent;
            // IsEnabled is deliberately NOT touched here. See the note above UpdateRowsInPlace.
            vm.TrackPendingRebuild(path, indexStatus?.State);
            item.State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus);
            item.ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-";
            configuredFolders.TryGetValue(path, out var saved);
            // Unlike a drive/WSL row (which stays visible as "an OS-resolvable thing you're just not
            // indexing" even after Delete), a folder row only exists because the user explicitly added
            // it -- there's no other reason to keep showing it once unchecked, whether or not it ever
            // got far enough to have a cache. So Delete is unconditional here, not gated on cache state.
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, _ => true);
        }
    }

    public static void RebuildRows(
        NetworkDriveSettingsViewModel vm, SearchService searchService, Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds,
        List<string> visibleDrives, List<string> visibleWsl, List<string> visibleFolders,
        Dictionary<string, NetworkIndexStatus> statuses, Dictionary<string, ResolvedNetworkDrive> resolvedByDrive, List<string> wslDistros,
        Dictionary<string, NetworkDriveSetting> configured, Dictionary<string, WslSetting> configuredWsl, Dictionary<string, FolderIndexSetting> configuredFolders)
    {
        foreach (var existing in vm.NetworkDrives) existing.PropertyChanged -= vm.OnNetworkDriveItemChanged;
        vm.NetworkDrives.Clear();
        foreach (var letter in visibleDrives)
        {
            statuses.TryGetValue(letter, out var indexStatus);
            resolvedByDrive.TryGetValue(letter, out var drive);
            var id = NetworkDriveResolver.GetNetworkId(letter);
            configured.TryGetValue(id, out var saved);
            var item = new NetworkDriveSettingsItem
            {
                Id = id,
                Drive = letter,
                IsPresent = drive != null,
                IsEnabled = drive != null && saved != null,
                State = drive == null ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(drive, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunDriveAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(letter, indexStatus?.State);
            vm.UpdateRowAction(item, drive != null && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.Drive));
            item.PropertyChanged += vm.OnNetworkDriveItemChanged;
            vm.NetworkDrives.Add(item);
        }

        foreach (var existing in vm.WslDrives) existing.PropertyChanged -= vm.OnWslDriveItemChanged;
        vm.WslDrives.Clear();
        foreach (var name in visibleWsl)
        {
            var unc = $@"\\wsl$\{name}";
            statuses.TryGetValue(unc, out var indexStatus);
            var isPresent = wslDistros.Contains(name, StringComparer.OrdinalIgnoreCase);
            configuredWsl.TryGetValue(name, out var saved);
            var item = new WslSettingsItem
            {
                Id = name,
                DistroName = name,
                IsPresent = isPresent,
                IsEnabled = isPresent && saved != null,
                State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunWslDriveAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(unc, indexStatus?.State);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, i => searchService.HasNetworkDriveCache(i.UncPath));
            item.PropertyChanged += vm.OnWslDriveItemChanged;
            vm.WslDrives.Add(item);
        }

        foreach (var existing in vm.FolderIndexes) existing.PropertyChanged -= vm.OnFolderItemChanged;
        vm.FolderIndexes.Clear();
        foreach (var path in visibleFolders)
        {
            statuses.TryGetValue(path, out var indexStatus);
            var isPresent = Directory.Exists(path);
            configuredFolders.TryGetValue(path, out var saved);
            var item = new FolderIndexSettingsItem
            {
                Path = path,
                IsPresent = isPresent,
                IsEnabled = isPresent && saved != null,
                State = !isPresent ? TranslationManager.Instance["Network_StatusUnavailable"] : NetworkDriveSettingsHelper.GetStateText(null, indexStatus),
                ItemCount = indexStatus?.Items > 0 ? $"{indexStatus.Items:N0}" : "-",
                RefreshMode = NetworkDriveSettingsHelper.NormalizeRefreshMode(saved?.RefreshMode)
            };
            item.RowActionCommand = new RelayCommand(() => NetworkDriveViewModelHelper.RunFolderIndexAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds), () => item.CanRunRowAction);
            vm.TrackPendingRebuild(path, indexStatus?.State);
            vm.UpdateRowAction(item, isPresent && saved != null, indexStatus?.State, _ => true);
            item.PropertyChanged += vm.OnFolderItemChanged;
            vm.FolderIndexes.Add(item);
        }
    }
}
