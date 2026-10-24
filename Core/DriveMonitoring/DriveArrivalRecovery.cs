using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

// One place for the decision a drive reaches when its volume stops answering: is it mounted but unreadable
// (a re-index can fix it now), or is it gone (nothing can fix it until it is plugged in again)?
//
// Every "it is gone" exit used to do was write a terminal state and return. Drive presence is only
// re-checked by whoever happens to poll GetStatus, and DriveReattachWaiter is only armed from a PnP
// remove-complete notification -- so a volume that left without ever producing one (a hard yank, an
// enclosure that only reports NOT_READY), a rebuild asked for while the disk was in a bag, or a scan
// interrupted by a removal all ended with no monitor and no catch-up, and with the in-memory index frozen
// at the moment the volume disappeared. That frozen index keeps answering queries -- showing rows for files
// that no longer exist and hiding files that do -- until the service restarts, with one log line as the
// only trace.
internal static class DriveArrivalRecovery
{
    internal static bool IsMounted(string drive) =>
        VolumeHelper.DetectIndexableLocalDrives().Contains(drive, StringComparer.OrdinalIgnoreCase);

    // Where a USN monitor's reopen budget runs out (see UsnMonitor.RecoverVolumeHandle): a minute of failed
    // reopens, and no Windows notification ever said this volume left.
    internal static void SettleMonitorGiveUp(
        UsnIndexer indexer, string drive, CancellationToken parentToken, Action<string>? onReindexRequired)
    {
        if (IsMounted(drive))
        {
            // Mounted but still unreadable is the same invalidation the two journal-error branches earlier
            // in the loop already treat as a re-index request: restore from cache with a journal catch-up,
            // or rebuild that drive when the cache can no longer be trusted.
            Logger.Log($"[DriveArrivalRecovery] Drive {drive} is mounted but never became readable again; requesting a re-index.", LogLevel.Error);
            onReindexRequired?.Invoke(drive);
            return;
        }

        Logger.Log($"[DriveArrivalRecovery] Drive {drive} left without a device notification; parking its index until it is re-inserted.", LogLevel.Error);
        ArmForReinsertion(indexer, drive, parentToken, onReindexRequired);
    }

    // Where a build ended with nothing to show for it: stopped by the user, or failed because the volume was
    // never there (no scan can run on an unmounted drive).
    internal static void SettleFailedBuild(
        UsnIndexer indexer, string drive, bool wasCancelled, CancellationToken parentToken, Action<string>? onReindexRequired)
    {
        if (IsMounted(drive))
        {
            // A Stop request reverts to "cached" (mirrors NetworkIndexer's own CancelDrive), not "failed"
            // -- the user asked for this, it isn't an error.
            indexer.SetDriveState(drive, wasCancelled ? "cached" : "failed");
            return;
        }

        ArmForReinsertion(indexer, drive, parentToken, onReindexRequired);
    }

    // The user asked to rebuild a volume that is not mounted. Nothing can be scanned yet, and the request
    // has to survive until it can: accepted only for a letter that already has a cached index on disk, so a
    // typo cannot leave a waiter polling for the life of the process.
    internal static bool ArmRebuildForReinsertion(
        UsnIndexer indexer, string cacheDir, string drive, CancellationToken parentToken, Action<string> onRebuild)
    {
        // GetCachePath/HasCache derive the file name from a live volume identity, which an unmounted drive
        // has none of, so the cache has to be found the way LocalDriveCacheLocator does for an unplugged
        // drive -- by reading the cached headers.
        if (!LocalDriveCacheLocator.ListCachedDrives(cacheDir)
                .Any(e => e.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Log($"[DriveArrivalRecovery] Ignored rebuild request for drive {drive}: not mounted and no cached index for it.", LogLevel.Warn);
            return false;
        }

        Logger.Log($"[DriveArrivalRecovery] Drive {drive} is not mounted; its rebuild will run as soon as it is inserted.");
        ArmForReinsertion(indexer, drive, parentToken, onRebuild);
        return true;
    }

    // Parks what is left of the drive and asks for it back. The frozen index is dropped rather than kept:
    // nothing can reconcile it any more, because no monitor reads that volume's journal while it is away and
    // every change made meanwhile is invisible here. What the waiter fires on re-insertion is the ordinary
    // restore-or-rebuild the caller hands in, which is what makes a disk that went out and came back
    // converge instead of staying stale for the rest of the session.
    //
    // ponytail: one waiter per call, polling every 2 s until the process ends, so repeated requests for the
    // same absent drive arm repeated waiters; the duplicate rebuild each of them asks for is refused by
    // SearchEngineDriveMaintenance's own per-drive dedupe. If that polling ever shows up in a profile, the
    // upgrade path is a per-drive set that dedupes the waiters themselves.
    internal static void ArmForReinsertion(
        UsnIndexer indexer, string drive, CancellationToken parentToken, Action<string>? onReindexRequired)
    {
        indexer.DropDriveFromRuntime(drive);
        indexer.SetDriveState(drive, "unavailable");
        DriveReattachWaiter.Start(drive, parentToken, () => onReindexRequired?.Invoke(drive));
    }
}
