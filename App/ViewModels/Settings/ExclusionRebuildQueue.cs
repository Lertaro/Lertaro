using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

// Saving the Exclusions page only sends the new rules to the service, which holds them in memory and
// applies them on the NEXT walk (see SearchServiceManagementExtensions.SaveMachineSettingsAsync /
// UsnIndexer.WalkOptions) -- a read that happens on the next walk. An already-built FAT32/exFAT index
// therefore keeps serving the subtree the user just excluded until something re-walks that drive, and
// nothing did: the rebuild only ever happened on an app restart or an explicit click on the Local page's
// Rebuild button. So a rules change queues one rebuild per affected drive here, as a side effect of the
// save.
//
// Queue-only, deliberately: the Local page's waiting variant (LocalDriveRebuildHelper) exists because a
// user explicitly asked for a rebuild and wants to watch it finish; blocking the settings dialog on a
// side effect of pressing Apply would be a dialog that hangs for minutes after unrelated edits.
//
// Journal-capable drives are left out because their build path ignores the exclusion rules entirely (an
// $MFT parse / ReFS scan reads the whole volume) -- rebuilding one would cost a multi-million-file walk
// to change nothing. That is the same split CheckNeedsLiveSearch makes; see the tier comment there.
internal static class ExclusionRebuildQueue
{
    /// <summary>
    /// Queues a rebuild for every enabled, non-journal-capable local drive, and returns immediately.
    /// </summary>
    /// <remarks>
    /// A no-op unless the rules actually changed, so an ordinary save (theme, window, hotkeys) never
    /// queues anything -- the caller passes the same "changed" signal it already computed for the
    /// mirror, rather than comparing the lists again here.
    /// </remarks>
    public static void QueueForChangedExclusions(
        SearchService searchService,
        MachineSettings machineSettings,
        IReadOnlyList<string> drives,
        bool exclusionsChanged)
    {
        if (!exclusionsChanged)
            return;

        foreach (var drive in drives.Where(d => ShouldRebuildForExclusionChange(IsEnabled(d, machineSettings), IsJournalCapable(d))))
        {
            // Fire and forget: the pipe round trip is async, and its only job is to hand the request to
            // the service. The status monitor already surfaces per-drive progress to the UI.
            _ = QueueAsync(searchService, drive);
        }
    }

    private static bool IsEnabled(string drive, MachineSettings machineSettings) =>
        machineSettings.IsLocalDriveEnabled(VolumeHelper.GetVolumeId(drive));

    private static bool IsJournalCapable(string drive) =>
        !string.IsNullOrWhiteSpace(drive) && VolumeHelper.SupportsUsnJournal(drive);

    /// <summary>
    /// The decision, split off from the two volume probes so it can be exercised without a real drive:
    /// only a drive that is actually indexed can have an index for the new rules to invalidate, and only
    /// a non-journal-capable one was walked with those rules applied in the first place.
    /// </summary>
    internal static bool ShouldRebuildForExclusionChange(bool isEnabled, bool isJournalCapable) =>
        isEnabled && !isJournalCapable;

    private static async Task QueueAsync(SearchService searchService, string drive)
    {
        try
        {
            // No waiting: the request is answered as soon as the service has queued the rebuild, and its
            // own status carries the drive through pending/indexing/ready for the UI to follow.
            await searchService.RebuildDriveIndexAsync(drive);
        }
        catch (Exception ex)
        {
            // A service that is restarting, or a dropped pipe, throws here. That must not take the
            // settings save down with it -- the rules are already persisted, so the next save or the
            // next launch retries this same rebuild.
            Logger.Log($"[Exclusions] Could not queue a rebuild of drive {drive} after a rules change: {ex.Message}", LogLevel.Warn);
        }
    }
}
