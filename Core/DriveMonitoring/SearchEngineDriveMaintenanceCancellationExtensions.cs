using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

// Cancels pending drive-maintenance operations.
internal static class SearchEngineDriveMaintenanceCancellationExtensions
{
    // Mirrors NetworkIndexer.CancelDrive for a local drive's own rebuild, or for the delayed retry it is
    // waiting on. A no-op (returns false) if neither exists for this drive right now.
    public static bool CancelDriveRebuild(this SearchEngineDriveMaintenance maintenance, string drive)
    {
        drive = DriveMaintenanceHelper.NormalizeDrive(drive);
        lock (maintenance._pendingDriveRebuilds)
        {
            if (maintenance.CancelDelayedRebuild(drive))
                return true;
            if (!maintenance._activeRebuildCts.TryGetValue(drive, out var cts) || cts is null)
                return false;

            // Cancelled inside the lock it was read under: RebuildDrive's finally removes and disposes
            // that same instance while holding it, so a Stop landing exactly as the rebuild completed used
            // to call Cancel() on a disposed source and throw ObjectDisposedException at the caller. The
            // only thing registered on this token is handle.Dispose, which does not come back here.
            cts.Cancel();
            return true;
        }
    }

    // A forced rebuild now, or after the one already running for this drive -- decided in one critical
    // section (see TryBeginRebuild), so a request racing another queue is never left as a refused duplicate.
    public static void QueueDriveRebuildAfterRemoval(this SearchEngineDriveMaintenance maintenance, string drive) =>
        maintenance.QueueDriveRebuild(DriveMaintenanceHelper.NormalizeDrive(drive), forceRebuild: true,
            followUpIfBusy: true);
}
