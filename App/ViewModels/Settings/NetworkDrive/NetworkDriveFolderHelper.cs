using System.IO;
using Lertaro.Core;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Views.Controls.Dialogs;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Folder-index half of NetworkDriveSettingsViewModel -- a third row category alongside NetworkDrives/
// WslDrives, riding the same NetworkIndexer/Scheduler machinery with a full folder path as the opaque
// key instead of a drive letter or WSL UNC path. Extracted (composition, not a partial class) to keep
// the main file under the line limit. The cross-category per-refresh sync (UpdateRowsInPlace/RebuildRows,
// which touch all three row categories together) lives in NetworkDriveRowSyncHelper instead -- this class
// stays scoped to the folder-only add-dialog workflow and visibility list its name promises.
internal static class NetworkDriveFolderHelper
{
    public static void AddFolder(NetworkDriveSettingsViewModel vm, SearchService searchService, Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        // A local drive root or WSL distro root belongs on the Network Drives/Local Drives/WSL tabs
        // (whole-volume indexing), not here. A UNC share root ("\\server\share") is let through, though
        // -- unlike a local drive, there's no drive-letter tab that can index an unmapped share at all,
        // so the share root is the finest-grained indexable unit available for it.
        if (IsDriveRoot(dialog.FolderName))
        {
            CustomMessageBox.Show(
                TranslationManager.Instance["Folder_RootNotAllowed"],
                TranslationManager.Instance["Executor_PromptTitle"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var path = dialog.FolderName.TrimEnd('\\');
        if (vm.FolderIndexes.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        var item = new FolderIndexSettingsItem { Path = path, IsEnabled = true, IsPresent = true };
        item.RowActionCommand = new RelayCommand(
            () => NetworkDriveViewModelHelper.RunFolderIndexAction(item, vm, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds),
            () => item.CanRunRowAction);
        item.PropertyChanged += vm.OnFolderItemChanged;
        vm.FolderIndexes.Add(item);
        vm.HasPendingEdits = true;
        vm.NotifyFolderIndexesEmptyChanged();
    }

    private static bool IsDriveRoot(string path)
    {
        try
        {
            var trimmed = path.TrimEnd('\\');
            // A non-WSL UNC path ("\\server\share", including its own root) has no drive-letter tab
            // that can index it, so it's never blocked here regardless of depth -- unlike a local
            // drive, even the share root itself is a legitimate folder-index target. A WSL path falls
            // through to the same root-vs-subfolder comparison below as a local drive: only the exact
            // distro root ("\\wsl$\Ubuntu") stays blocked (it already has its own tab), a subfolder
            // within it ("\\wsl$\Ubuntu\home\user\projects") is just as legitimate a target as a UNC
            // share subfolder.
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) && !NetworkDriveSettingsHelper.IsWslPath(trimmed))
                return false;

            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && trimmed.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Configured (user-added, from UserSettings) union'd with anything the cache still remembers -- so a
    // folder whose entry got unchecked but not deleted still shows a Delete row, mirroring visibleWsl. Also
    // union'd with whatever's live in FolderIndexes itself -- a row AddFolder just created has neither a
    // UserSettings entry (nothing's been Applied yet) nor a cache (never scanned), so without this it would
    // never appear in this list at all and UpdateRowsInPlace would silently never touch it again: no state
    // text, no item count, no row action, forever, until Apply -- and no way to back out of the addition
    // without going through Apply first.
    public static List<string> GetVisibleFolders(NetworkDriveSettingsViewModel vm, SearchService searchService, UserSettings userSettings)
    {
        var configuredPaths = userSettings.FolderIndexes
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .Select(f => f.Path.TrimEnd('\\'));
        // A folder-index key is longer than a bare drive letter and isn't a WSL distro's "\\wsl$\..." key
        // -- a genuine UNC share key ("\\server\share", now indexable via the folder-index feature) is
        // NOT excluded here, only WSL is.
        var cachedPaths = searchService.GetCachedNetworkDrives()
            .Where(d => d.Length > 1 && !NetworkDriveSettingsHelper.IsWslPath(d));
        var livePaths = vm.FolderIndexes.Select(f => f.Path);
        return configuredPaths.Concat(cachedPaths).Concat(livePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
