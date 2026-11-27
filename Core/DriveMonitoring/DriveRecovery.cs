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
        // A FAT32/exFAT drive whose index is still loaded -- a re-walk for a lost watcher, a follow-up, a
        // manual rebuild -- keeps serving it while the walk runs, and the walk diffs against it. Reloading the
        // cache here rolled search back to the last compaction (that drive's whole uncompacted delta, since
        // changes are no longer persisted one by one) for as long as the walk took. A drive that returned
        // after a removal has none loaded (DropDriveFromRuntime), so it still restores from its cache.
        bool loaded;
        lock (indexer.LockObj)
            loaded = indexer._recordIndexes.ContainsKey(drive);
        var cached = loaded && !VolumeHelper.SupportsUsnJournal(drive) ? null : indexer.TryLoadDriveFromCache(cacheDir, drive);
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
                    TrySaveDriveCache(indexer, cacheDir, new() { (drive, cached.Value.JournalId, nextUsn) }, drive, "USN catch-up", onReindexRequired);
                    DriveMonitorFactory.EnsureMonitor(indexer, drive, cached.Value.JournalId, nextUsn, token, rebuildToken, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);
                    Logger.Log($"[SearchEngine] Restored drive {drive} from cache and USN catch-up.");
                    return;
                }

                // Catch-up failed (journal mismatch/error) -- this cache can't be trusted even as a diff
                // baseline, unlike the "merely incomplete" case above, so drop it before rebuilding.
                indexer.DropDriveFromRuntime(drive);
            }
        }

        Logger.Log($"[SearchEngine] Rebuilding drive {drive} only (its cache was not restored, or its loaded index is kept as the baseline).");
        // Stop only the live watcher before rebuilding. Keep the device notification registered so a
        // physical removal during this rebuild can cancel its scan and schedule recovery.
        if (VolumeHelper.SupportsUsnJournal(drive))
            indexer.ReleaseDriveMonitor(drive);
        var wasCancelled = false;
        var metadata = indexer.BuildDrives(new[] { drive }, clearExisting: false, cacheDir: cacheDir,
            getToken: _ => rebuildToken, onDriveCancelled: _ => wasCancelled = true);
        if (metadata.Count == 0)
        {
            // Settles as "cached" (a Stop, not an error), "failed", or -- when the volume is simply not
            // mounted any more -- a drive parked for re-insertion. See DriveArrivalRecovery for why the
            // last one must not end here.
            DriveArrivalRecovery.SettleFailedBuild(indexer, drive, wasCancelled, rebuildToken, token, onReindexRequired, onReindexRequired);
            return;
        }

        // No cancellation check from here on: the build has already swapped its new index in, so stopping now
        // would stop nothing, and skipping the hand-over would leave that index without every change made
        // during its walk. A recording the cancel cut short asks for its own re-walk at the hand-over.
        foreach (var (builtDrive, journalId, nextUsn) in metadata)
            DriveMonitorFactory.EnsureMonitor(indexer, builtDrive, journalId, nextUsn, token, rebuildToken, onReindexRequired, onRemovalRequested, onReindexAfterRemoval);

        // A change the drive's old monitor applied to the doomed old LiveIndex during the rebuild (see
        // UsnIndexerExtensions.ApplyFolderChange), or a USN batch that found no LiveIndex while the rebuild
        // swapped it (ApplyUsnRecords), flags the drive: queue one follow-up so the next walk observes it. A
        // FAT32/exFAT drive's hand-over has already cleared the flag, since its recording covered the walk.
        if (indexer.ConsumeMissedFolderChangeDuringRebuild(drive))
            onReindexRequired?.Invoke(drive);
    }

    private static void TrySaveDriveCache(
        UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> metadata,
        string drive,
        string stage,
        Action<string>? onReindexRequired)
    {
        if (metadata.Count == 0)
            return;
        try
        {
            indexer.SaveDrivesToCache(cacheDir, metadata, onReindexRequired);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to save cache for drive {drive} after {stage}: {ex.Message}", LogLevel.Warn);
        }
    }
}
