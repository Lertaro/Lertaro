using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Per-category summary text (NetworkIndexSummary/WslIndexSummary/FolderIndexSummary) for
// NetworkDriveSettingsViewModel -- extracted (composition, not a partial class) purely to keep that
// file under the repo's per-file line limit. Each category computes its own enabled count, item total,
// and busy state independently -- never a combined total across all three, which used to read as
// nonsense on whichever tab wasn't the one actually busy/enabled.
internal static class NetworkDriveSummaryHelper
{
    public static void UpdateSummaries(NetworkDriveSettingsViewModel vm, IReadOnlyList<NetworkIndexStatus>? indexStatuses, bool driveBusy, bool wslBusy, bool folderBusy)
    {
        vm.NetworkIndexSummary = BuildSummary(
            vm.NetworkDrives.Count == 0, "Network_DrivesEmpty",
            vm.NetworkDrives.Count(d => d.AppliedEnabled),
            SumItems(indexStatuses, vm.NetworkDrives.Select(d => d.Drive)),
            driveBusy);

        // WslDrives.Count == 0 never actually happens while this summary is visible (the WSL tab only
        // renders when IsWslPanelVisible, i.e. at least one distro exists) -- guarded anyway rather than
        // assume the caller always agrees.
        vm.WslIndexSummary = BuildSummary(
            vm.WslDrives.Count == 0, "Network_DrivesEmpty",
            vm.WslDrives.Count(w => w.AppliedEnabled),
            SumItems(indexStatuses, vm.WslDrives.Select(w => $@"\\wsl$\{w.DistroName}")),
            wslBusy);

        vm.FolderIndexSummary = BuildSummary(
            vm.IsFolderIndexesEmpty, "Folder_IndexEmpty",
            vm.FolderIndexes.Count(f => f.AppliedEnabled),
            SumItems(indexStatuses, vm.FolderIndexes.Select(f => f.Path)),
            folderBusy);
    }

    private static string BuildSummary(bool isEmpty, string emptyKey, int enabledCount, int totalItems, bool busy)
    {
        if (isEmpty)
            return TranslationManager.Instance[emptyKey];

        var state = busy ? TranslationManager.Instance["Network_StatusIndexing"] : TranslationManager.Instance["Status_Ready"];
        return string.Format(TranslationManager.Instance["Network_SummaryTemplate"], state, enabledCount, totalItems);
    }

    private static int SumItems(IReadOnlyList<NetworkIndexStatus>? indexStatuses, IEnumerable<string> keys)
    {
        var keySet = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return (indexStatuses ?? Array.Empty<NetworkIndexStatus>()).Where(s => keySet.Contains(s.Drive)).Sum(s => s.Items);
    }
}
