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
    /// <summary>
    /// Refreshes the live rows without rebuilding them, for a refresh where the set of rows itself did
    /// not change.
    /// </summary>
    /// <remarks>
    /// This deliberately does not write IsEnabled. It used to clear it for any row whose target was not
    /// currently reachable, which took a transient probe and used it to overwrite a persisted user
    /// decision. That was reported as a LAN folder losing its tick after a laptop came back home, and
    /// the tick was the least of it -- the whole chain ran without the user ever touching a checkbox:
    ///
    /// IsEnabled is set through SetProperty, so clearing it raised PropertyChanged, which set
    /// HasPendingEdits (see NetworkDriveSettingsViewModel.OnFolderItemChanged) -- an edit the user never
    /// made. HasPendingEdits then pins NetworkDriveRefreshCoordinator to this method for the rest of the
    /// session, locking out RebuildRows, which is the one path that recomputes the tick correctly from
    /// the saved settings. And the next Apply of anything at all filters rows by IsEnabled
    /// (SettingsViewModel) and replaces the settings list wholesale, so the entry was silently deleted
    /// from the user's configuration. Coming back, the row read as never configured, and ticking it was
    /// a fresh add: a full re-scan from zero rather than the re-enable it looked like.
    ///
    /// The enabled state is already persisted -- as whether a settings entry exists at all -- and
    /// reachability already has its own two outlets, IsPresent and the state text, plus CanEditEnabled
    /// to grey the box out. That is exactly what the Local Drives tab does
    /// (LocalDriveSettingsViewModel: an existing row keeps its own IsEnabled, and isPresent only feeds
    /// CanEditEnabled), and these three loops were the ones going around it.
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
