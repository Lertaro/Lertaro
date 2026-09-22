using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

// Extracted to keep SearchEngineDriveMaintenance.cs under the project's line limit -- matches the
// UsnIndexerMonitorExtensions.cs split pattern already used elsewhere for the same reason.
internal static class SearchEngineDriveMaintenanceCancellationExtensions
{
    // Mirrors NetworkIndexer.CancelDrive for a local drive's own rebuild -- no-op (returns false) if
    // nothing is actually in flight for this drive right now.
    public static bool CancelDriveRebuild(this SearchEngineDriveMaintenance maintenance, string drive)
    {
        drive = DriveMaintenanceHelper.NormalizeDrive(drive);
        lock (maintenance._pendingDriveRebuilds)
        {
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

    public static void QueueDriveRebuildAfterRemoval(this SearchEngineDriveMaintenance maintenance, string drive)
    {
        drive = DriveMaintenanceHelper.NormalizeDrive(drive);
        lock (maintenance._pendingDriveRebuilds)
        {
            if (maintenance._pendingDriveRebuilds.Contains(drive))
            {
                maintenance._rebuildAfterRemoval.Add(drive);
                return;
            }
        }
        maintenance.QueueDriveRebuild(drive, forceRebuild: true);
    }
}
