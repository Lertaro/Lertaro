using Lertaro.Core;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

internal sealed record LocalDriveSnapshot(string Drive, string Id, bool IsEnabled);

// Comparison/rebuild helpers used only by SettingsViewModel.Apply() -- split out to keep that file
// under the line-count limit.
internal static class SettingsApplyHelpers
{
    public static async Task RebuildScanBasedLocalDrivesAsync(SearchService searchService, IReadOnlyList<LocalDriveSnapshot> drives, IReadOnlyList<string> enabledLocalDriveIds)
    {
        var enabled = enabledLocalDriveIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in drives.Where(d => ShouldRebuildScanBasedLocalDrive(d, enabled)))
        {
            var fs = VolumeHelper.GetFileSystemType(drive.Drive);
            if (!fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) &&
                await searchService.RebuildDriveIndexAsync(drive.Drive))
                await WaitForLocalDriveRebuildAsync(searchService, drive.Drive);
        }
    }

    internal static bool ShouldRebuildScanBasedLocalDrive(LocalDriveSnapshot drive, IReadOnlySet<string> enabledIds) =>
        drive.IsEnabled && enabledIds.Contains(drive.Id);

    private static async Task WaitForLocalDriveRebuildAsync(SearchService searchService, string drive)
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            var status = await searchService.GetStatusAsync();
            var item = status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (item?.State is not ("pending" or "indexing"))
                return;
        }
    }

    public static bool NetworkSettingsChanged(IReadOnlyList<NetworkDriveSetting> oldSettings, IReadOnlyList<NetworkDriveSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    public static bool WslSettingsChanged(IReadOnlyList<WslSetting> oldSettings, IReadOnlyList<WslSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(d => $"{d.Id}|{d.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }

    public static bool FolderIndexesChanged(IReadOnlyList<FolderIndexSetting> oldSettings, IReadOnlyList<FolderIndexSetting> newSettings)
    {
        var oldOrdered = oldSettings
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Path}|{f.RefreshMode}");

        var newOrdered = newSettings
            .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => $"{f.Path}|{f.RefreshMode}");
        return !oldOrdered.SequenceEqual(newOrdered, StringComparer.OrdinalIgnoreCase);
    }
}
