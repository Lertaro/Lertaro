using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Services.Search;

// Split out so the readiness decision is testable without starting a named-pipe service or waiting on
// real index files. The caller still owns the bounded polling loop and cancellation behavior.
internal static class DirectoryIndexReadiness
{
    public static bool IsLocalReady(UsnIndexer.IndexerStatus status, string drive)
        => status.State != "error"
            && status.Drives.Any(item => string.Equals(item.Drive, drive, StringComparison.OrdinalIgnoreCase)
                && item.State == "ready");

    public static bool IsInProcessReady(NetworkIndexStatus status)
        => status.State is "ready" or "cached";

    public static bool ShouldWaitForLocal(UsnIndexer.IndexerStatus status, string drive)
    {
        if (status.State == "error")
            return false;

        var driveStatus = status.Drives.FirstOrDefault(item =>
            string.Equals(item.Drive, drive, StringComparison.OrdinalIgnoreCase));
        return driveStatus?.State is "pending" or "indexing"
            || driveStatus == null && status.State is "pending" or "indexing";
    }
}
