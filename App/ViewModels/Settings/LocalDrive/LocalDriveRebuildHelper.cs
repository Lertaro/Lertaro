using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings.LocalDrive;

internal static class LocalDriveRebuildHelper
{
    public static async Task RebuildEnabledDrivesAsync(
        SearchService searchService,
        IReadOnlyList<LocalDriveSettingsItem> drives,
        Func<string, bool> isEnabled,
        Action<string> onQueued)
    {
        var rebuildTasks = new List<Task>();
        foreach (var drive in drives.Where(d => isEnabled(d.Drive)))
        {
            onQueued(drive.Drive);
            rebuildTasks.Add(RebuildDriveAsync(searchService, drive.Drive));
        }

        await Task.WhenAll(rebuildTasks);
    }

    private static async Task RebuildDriveAsync(SearchService searchService, string drive)
    {
        if (!await searchService.RebuildDriveIndexAsync(drive))
            return;

        await WaitForDriveRebuildAsync(searchService, drive);
    }

    private static async Task WaitForDriveRebuildAsync(SearchService searchService, string drive)
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
}
