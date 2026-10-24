using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Indexer.Usn;

// Split out to keep UsnIndexerExtensions.cs under the repository's per-file line limit; catch-up
// policy and application are independent concerns from live USN record mutation.
public static class UsnIndexerCatchUpExtensions
{
    public static long CatchUpDrive(this UsnIndexer indexer, string drive, ulong journalId, long startUsn, CancellationToken token = default)
    {
        indexer.BeginReplay(drive);
        try
        {
            return indexer.CatchUpDriveCore(drive, journalId, startUsn, token);
        }
        finally
        {
            indexer.EndReplay(drive);
        }
    }

    private static long CatchUpDriveCore(this UsnIndexer indexer, string drive, ulong journalId, long startUsn, CancellationToken token)
    {
        var changes = new List<ParsedUsnRecord>();
        var limitReached = false;
        var nextUsn = indexer._reader.CatchUpDrive(drive, journalId, startUsn, record =>
        {
            if (!UsnCatchUpPolicy.CanAccept(changes.Count))
            {
                limitReached = true;
                return false;
            }

            changes.Add(record);
            return true;
        }, token);
        if (limitReached)
        {
            Logger.Log($"[UsnIndexer] Catch-up for drive {drive} exceeded {UsnCatchUpPolicy.MaxRecords} records; requiring a full re-index.", LogLevel.Warn);
            return -1;
        }

        if (nextUsn >= 0 && changes.Count > 0)
        {
            // One replay batch, one notification: the batch itself suppresses the per-batch directory
            // fan-out that a live stream needs, because a catch-up is typically tens of thousands of
            // records describing folders the app is not watching yet anyway. Only notify when the batch
            // actually landed -- a drive whose LiveIndex is gone has nothing to tell subscribers about.
            if (indexer.ApplyUsnRecords(drive, changes, UsnApplyOrigin.Replay))
            {
                indexer.RaiseDirectoriesChanged(drive, null);
                SearchCoordinator.ClearCaches();
            }
        }

        return nextUsn;
    }

    // A catch-up is the one moment the index is applying a large backlog of records it did not see
    // arrive, so it owns the drive for the duration: idle compaction reads the flag below to stay out of
    // the way rather than taking the LiveIndex write lock across a full snapshot rewrite mid-drain.
    internal static void BeginReplay(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            indexer._replayingDrives.Add(drive);
    }

    internal static void EndReplay(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            indexer._replayingDrives.Remove(drive);
    }

    internal static bool IsReplayInProgress(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            return indexer._replayingDrives.Contains(drive);
    }
}
