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
        CancellationTokenSource? cts;
        lock (maintenance._pendingDriveRebuilds)
            maintenance._activeRebuildCts.TryGetValue(drive, out cts);
        if (cts == null)
            return false;

        cts.Cancel();
        return true;
    }
}
