using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Indexer.Usn;

// Split out to keep UsnIndexerExtensions.cs under the repository's per-file line limit; catch-up
// policy and application are independent concerns from live USN record mutation.
public static class UsnIndexerCatchUpExtensions
{
    public static long CatchUpDrive(this UsnIndexer indexer, string drive, ulong journalId, long startUsn)
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
        });
        if (limitReached)
        {
            Logger.Log($"[UsnIndexer] Catch-up for drive {drive} exceeded {UsnCatchUpPolicy.MaxRecords} records; requiring a full re-index.", LogLevel.Warn);
            return -1;
        }

        if (nextUsn >= 0 && changes.Count > 0)
            indexer.ApplyUsnRecords(drive, changes);

        return nextUsn;
    }
}
