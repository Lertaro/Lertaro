using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Per-row editability/action permissions for NetworkDriveSettingsViewModel -- extracted (composition,
// not a partial class) purely to keep that file under the repo's per-file line limit.
internal static class NetworkDrivePermissionsHelper
{
    // Each category gets its own busy flag (see the driveBusy/wslBusy/folderBusy computation in
    // NetworkDriveSettingsViewModel.RefreshNetworkDrives) so an indexing folder can't disable a network
    // drive's row controls, or the reverse -- including the Rebuild-row-action gate below, which used to
    // reference a single global CanRebuild (hasEnabled across all three categories && nothing anywhere
    // busy) regardless of which category the row actually belonged to.
    public static void UpdateRowPermissions(NetworkDriveSettingsViewModel vm, bool driveBusy, bool wslBusy, bool folderBusy)
    {
        var canRebuildDrives = vm.NetworkDrives.Any(d => d.AppliedEnabled) && !driveBusy;
        foreach (var drive in vm.NetworkDrives)
        {
            drive.CanEditEnabled = drive.IsPresent && !driveBusy;
            drive.CanEditRefreshMode = drive.IsPresent && !driveBusy;
            // Stop stays clickable through driveBusy -- a Stop row is exactly what's causing it.
            drive.CanRunRowAction = drive.RowAction == NetworkDriveRowAction.Stop
                || (!driveBusy && (drive.RowAction == NetworkDriveRowAction.Delete || canRebuildDrives && drive.RowAction == NetworkDriveRowAction.Rebuild));
        }
        var canRebuildWsl = vm.WslDrives.Any(w => w.AppliedEnabled) && !wslBusy;
        foreach (var wsl in vm.WslDrives)
        {
            wsl.CanEditEnabled = wsl.IsPresent && !wslBusy;
            wsl.CanEditRefreshMode = wsl.IsPresent && !wslBusy;
            wsl.CanRunRowAction = wsl.RowAction == NetworkDriveRowAction.Stop
                || (!wslBusy && (wsl.RowAction == NetworkDriveRowAction.Delete || canRebuildWsl && wsl.RowAction == NetworkDriveRowAction.Rebuild));
        }
        foreach (var folder in vm.FolderIndexes)
        {
            folder.CanEditEnabled = folder.IsPresent && !folderBusy;
            folder.CanEditRefreshMode = folder.IsPresent && !folderBusy;
            // Delete also stays clickable through folderBusy, unlike drives/WSL: folderBusy already
            // excludes isGlobalBusy (!isServiceReady, the *local USN* service), which has nothing to do
            // with folder indexing (it runs entirely in-process, never through that service) -- removing a
            // folder row that was never applied/cached must not get blocked by an unrelated service being
            // unreachable.
            folder.CanRunRowAction = folder.RowAction is NetworkDriveRowAction.Stop or NetworkDriveRowAction.Delete
                || (!folderBusy && vm.CanRebuildFolders && folder.RowAction == NetworkDriveRowAction.Rebuild);
        }
    }

    // Scoped membership check: is anything in this one category (by its own drive/UNC/folder keys)
    // currently queued-to-rebuild or actively indexing/pending -- as opposed to a single isBusy that
    // looked at indexStatuses across all three categories combined.
    public static bool IsCategoryBusy(HashSet<string> pendingRowRebuilds, IEnumerable<string> keys, IReadOnlyList<NetworkIndexStatus>? indexStatuses)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        if (pendingRowRebuilds.Any(keySet.Contains))
            return true;
        return indexStatuses?.Any(s => keySet.Contains(s.Drive) && (s.State == "indexing" || s.State == "pending")) == true;
    }
}
