using Lertaro.App.ViewModels.Settings.NetworkDrive;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// The half of Apply that talks to the service, run off the UI thread. Everything the user staged has
/// already been written to UserSettings by the time this starts, so what is left is telling the running
/// indexer what changed -- a machine-settings write, a network index refresh, and a scan-based rebuild.
/// </summary>
/// <param name="SearchService">The client to the elevated service this apply talks to.</param>
/// <param name="Snapshot">The before/after state captured when Apply started.</param>
/// <param name="ExclusionsChanged">Whether the exclusion rules moved, which forces a full rebuild.</param>
/// <param name="AliasProviderEnabled">Whether an alias provider came back on, which needs the index reloaded.</param>
/// <param name="OnFinished">Runs on the UI thread's own schedule when the pipeline ends, either way.</param>
internal sealed record SettingsApplyBackground(
    SearchService SearchService,
    SettingsApplySnapshot Snapshot,
    bool ExclusionsChanged,
    bool AliasProviderEnabled,
    Action OnFinished)
{
    internal void Run() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        try
        {
            var previousLocalDrives = (await SearchService.GetMachineSettingsAsync()).LocalDrives.ToList();
            if (SettingsChangeSnapshot.StringListChanged(previousLocalDrives, Snapshot.MachineSettings.LocalDrives))
                await SearchService.SaveMachineSettingsAsync(Snapshot.MachineSettings);

            if (ExclusionsChanged)
            {
                SearchService.RefreshNetworkIndexes();
            }
            else if (SettingsApplyHelpers.NetworkSettingsChanged(Snapshot.PreviousNetworkDrives, Snapshot.NewNetworkDrives)
                || SettingsApplyHelpers.WslSettingsChanged(Snapshot.PreviousWslDrives, Snapshot.NewWslDrives)
                || SettingsApplyHelpers.FolderIndexesChanged(Snapshot.PreviousFolderIndexes, Snapshot.NewFolderIndexes))
            {
                await NetworkDriveApplyHelper.ApplyChangesAsync(SearchService, Snapshot.PreviousNetworkDrives, Snapshot.NewNetworkDrives);
                foreach (var wsl in Snapshot.NewWslDrives)
                {
                    if (!Snapshot.PreviousWslDrives.Any(w => w.Id.Equals(wsl.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        var unc = $@"\\wsl$\{wsl.Id}";
                        SearchService.RefreshNetworkDriveIndex(unc);
                    }
                }
                // Unlike a network drive, a folder path never needs resolving from the OS, so there's
                // nothing to wait for -- ConfigureNetworkIndexes() (already called above via
                // ApplyChangesAsync) already auto-queues an initial refresh for it; this just requests it
                // directly, same as a newly-added WSL distro above.
                foreach (var folder in Snapshot.NewFolderIndexes)
                {
                    if (!Snapshot.PreviousFolderIndexes.Any(f => f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase)))
                        SearchService.RefreshNetworkDriveIndex(folder.Path);
                }
            }

            if (ExclusionsChanged)
                await SettingsApplyHelpers.RebuildScanBasedLocalDrivesAsync(SearchService, Snapshot.LocalDriveSnapshots, Snapshot.MachineSettings.LocalDrives);

            if (AliasProviderEnabled)
                await SearchService.InitializeOrLoadIndexAsync(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[Settings] Apply pipeline failed: {ex}", LogLevel.Error);
        }
        finally
        {
            OnFinished();
        }
    }
}
