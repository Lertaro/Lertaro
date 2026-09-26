using Lertaro.App.ViewModels.Settings.LocalDrive;
using Lertaro.App.ViewModels.Settings.NetworkDrive;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// The before/after state one Apply compares to decide what actually changed, captured in one place and
/// up front -- before any of it is written, because the writes overwrite the "before".
/// </summary>
/// <param name="PreviousNetworkDrives">Persisted network drives, as they were when Apply started.</param>
/// <param name="PreviousWslDrives">Persisted WSL distros, as they were when Apply started.</param>
/// <param name="PreviousFolderIndexes">Persisted folder indexes, as they were when Apply started.</param>
/// <param name="PreviousExclusions">Exclusion rules, as they were when Apply started.</param>
/// <param name="PreviousDisabledAliases">Disabled alias components, as they were when Apply started.</param>
/// <param name="MachineSettings">The local drives the pages say should be indexed now.</param>
/// <param name="NewNetworkDrives">The network drives to persist.</param>
/// <param name="NewWslDrives">The WSL distros to persist.</param>
/// <param name="NewFolderIndexes">The folder indexes to persist.</param>
/// <param name="LocalDriveSnapshots">Every local drive row with its enablement, for the rebuild path.</param>
internal sealed record SettingsApplySnapshot(
    List<NetworkDriveSetting> PreviousNetworkDrives,
    List<WslSetting> PreviousWslDrives,
    List<FolderIndexSetting> PreviousFolderIndexes,
    ExclusionSnapshot PreviousExclusions,
    List<string> PreviousDisabledAliases,
    MachineSettings MachineSettings,
    List<NetworkDriveSetting> NewNetworkDrives,
    List<WslSetting> NewWslDrives,
    List<FolderIndexSetting> NewFolderIndexes,
    List<LocalDriveSnapshot> LocalDriveSnapshots)
{
    /// <summary>
    /// Reads the persisted state, then projects the pages' staged rows into the shapes UserSettings holds.
    /// A row that is disabled or unnamed is dropped rather than persisted, which is also what makes the
    /// "changed" comparisons in SettingsApplyHelpers see a removal.
    /// </summary>
    internal static SettingsApplySnapshot Capture(
        UserSettings settings,
        LocalDriveSettingsViewModel localDrive,
        NetworkDriveSettingsViewModel networkDrive) => new(
            PreviousNetworkDrives: [.. settings.NetworkDrives.Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })],
            PreviousWslDrives: [.. settings.WslSettings.Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })],
            PreviousFolderIndexes: [.. settings.FolderIndexes.Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })],
            PreviousExclusions: SettingsChangeSnapshot.CaptureExclusions(settings),
            PreviousDisabledAliases: [.. settings.DisabledPluginComponents.Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))],
            MachineSettings: new MachineSettings
            {
                LocalDrives = [.. localDrive.LocalDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase)]
            },
            NewNetworkDrives: [.. networkDrive.NetworkDrives.Where(d => d.IsEnabled && !string.IsNullOrWhiteSpace(d.Id)).Select(d => new NetworkDriveSetting { Id = d.Id, RefreshMode = d.RefreshMode })],
            NewWslDrives: [.. networkDrive.WslDrives.Where(w => w.IsEnabled && !string.IsNullOrWhiteSpace(w.Id)).Select(w => new WslSetting { Id = w.Id, RefreshMode = w.RefreshMode })],
            NewFolderIndexes: [.. networkDrive.FolderIndexes.Where(f => f.IsEnabled && !string.IsNullOrWhiteSpace(f.Path)).Select(f => new FolderIndexSetting { Path = f.Path, RefreshMode = f.RefreshMode })],
            LocalDriveSnapshots: [.. localDrive.LocalDrives.Select(d => new LocalDriveSnapshot(d.Drive, d.Id, d.IsEnabled))]);
}
