using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

internal static class DriveRecovery
{
    public static void RestoreOrRebuild(
        UsnIndexer indexer,
        string cacheDir,
        string drive,
        CancellationToken token,
        Action<string>? onReindexRequired,
        CancellationToken rebuildToken = default,
        Action<string>? onRemovalRequested = null,
        Action<string>? onReindexAfterRemoval = null)
    {
        Logger.Log($"[SearchEngine] Restoring newly available drive {drive} from cache if possible.");
        var cached = indexer.TryLoadDriveFromCache(cacheDir, drive);
        // A cache from a scan interrupted before finishing (crash/restart mid-walk, not a graceful Stop)
        // must not be mistaken for "restored, nothing more to do" -- see UsnIndexer.IsDriveIndexComplete's
        // own comment. Skips straight past the catch-up/folder-restore short-circuits below to the rebuild
        // at the bottom, WITHOUT dropping the runtime first (unlike the catch-up-failed case), so that
        // rebuild picks up this incomplete LiveIndex as a TreeDiffBaseline resume point.
        if (cached.HasValue && indexer.IsDriveIndexComplete(drive))
        {
            if (!VolumeHelper.SupportsUsnJournal(drive))
            {
                // A folder watcher cannot observe changes made while the volume was detached. Keep the
                // loaded snapshot as the scan baseline, but run the scan before starting a new watcher.
                Logger.Log($"[SearchEngine] Folder-scan drive {drive} returned; refreshing its cached index.");
            }
            else
            {
                var nextUsn = indexer.CatchUpDrive(drive, cached.Value.JournalId, cached.Value.NextUsn, rebuildToken);
                if (nextUsn >= 0)
                {
                    TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, nextUsn) }, drive, "USN catch-up");
                    DriveMonitorFactory.EnsureMonitor(indexer, drive, cached.Value.JournalId, nextUsn, token, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);
                    Logger.Log($"[SearchEngine] Restored drive {drive} from cache and USN catch-up.");
                    return;
                }

                // Catch-up failed (journal mismatch/error) -- this cache can't be trusted even as a diff
                // baseline, unlike the "merely incomplete" case above, so drop it before rebuilding.
                indexer.DropDriveFromRuntime(drive);
            }
        }

        Logger.Log($"[SearchEngine] Cache restore unavailable for drive {drive}; rebuilding this drive only.");
        // Stop only the live watcher before rebuilding. Keep the device notification registered so a
        // physical removal during this rebuild can cancel its scan and schedule recovery.
        if (VolumeHelper.SupportsUsnJournal(drive))
            indexer.ReleaseDriveMonitor(drive);
        var wasCancelled = false;
        var metadata = indexer.BuildDrives(new[] { drive }, clearExisting: false, cacheDir: cacheDir,
            getToken: _ => rebuildToken, onDriveCancelled: _ => wasCancelled = true);
        if (metadata.Count == 0)
        {
            // A Stop request reverts to "cached" (mirrors NetworkIndexer's own CancelDrive), not "failed"
            // -- the user asked for this, it isn't an error.
            var present = VolumeHelper.DetectIndexableLocalDrives().Contains(drive, StringComparer.OrdinalIgnoreCase);
            indexer.SetDriveState(drive, wasCancelled ? (present ? "cached" : "unavailable") : "failed");
            return;
        }

        foreach (var (builtDrive, journalId, nextUsn) in metadata)
            DriveMonitorFactory.EnsureMonitor(indexer, builtDrive, journalId, nextUsn, token, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);

        // The drive's own monitor stayed alive throughout the rebuild (see
        // UsnIndexerExtensions.ApplyFolderChange); if it detected a change it couldn't persist against
        // the doomed old LiveIndex, queue one follow-up refresh so the next walk observes it. A journal
        // drive (monitor already stopped above) never sets this flag in the first place.
        if (indexer.ConsumeMissedFolderChangeDuringRebuild(drive))
            onReindexRequired?.Invoke(drive);
    }

    private static void TrySaveDriveCache(
        UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> metadata,
        string drive,
        string stage)
    {
        if (metadata.Count == 0)
            return;
        try
        {
            indexer.SaveDrivesToCache(cacheDir, metadata);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to save cache for drive {drive} after {stage}: {ex.Message}", LogLevel.Warn);
        }
    }
}
