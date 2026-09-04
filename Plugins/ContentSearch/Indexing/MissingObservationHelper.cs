using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Applies the two-phase deletion policy for files that disappear from reachable monitored
/// folders. Split out of ContentIndexScheduler purely to keep that file under the repository's
/// per-file line limit; this helper holds no state and operates on the database passed in.
/// </summary>
public static class MissingObservationHelper
{
    private const int RetryLimit = 3;

    public static MissingObservationResult ApplyRetention(
        ContentSearchDatabase database,
        Dictionary<string, (long LastModified, long FileSize, int MissingCount)> existingMeta,
        HashSet<string> discovered,
        ContentIndexConfig reachableConfig,
        IReadOnlyCollection<string>? observedDirectories = null)
    {
        var missingPaths = existingMeta.Keys
            .Where(p => !discovered.Contains(p)
                && ContentIndexScheduler.IsFileInMonitoredFolders(p, reachableConfig)
                && IsInObservedDirectories(p, observedDirectories))
            .ToList();

        var toDelete = new List<string>();
        var toUpdateMissing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in missingPaths)
        {
            var newCount = existingMeta[path].MissingCount + 1;
            if (newCount >= RetryLimit)
            {
                toDelete.Add(path);
            }
            else
            {
                toUpdateMissing[path] = newCount;
            }
        }

        if (toDelete.Count > 0)
        {
            database.DeleteFilesBatch(toDelete);
        }

        if (toUpdateMissing.Count > 0)
        {
            database.UpdateMissingCounts(toUpdateMissing);
        }

        var toResetMissing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in existingMeta)
        {
            if (discovered.Contains(pair.Key) && pair.Value.MissingCount != 0)
            {
                toResetMissing[pair.Key] = 0;
            }
        }

        if (toResetMissing.Count > 0)
        {
            database.UpdateMissingCounts(toResetMissing);
        }

        return new MissingObservationResult(toDelete.Count, toUpdateMissing.Count);
    }

    private static bool IsInObservedDirectories(string path, IReadOnlyCollection<string>? observedDirectories)
    {
        if (observedDirectories == null)
            return true;

        foreach (var directory in observedDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            var root = directory.EndsWith(Path.DirectorySeparatorChar) || directory.EndsWith(Path.AltDirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

public readonly record struct MissingObservationResult(int Pruned, int KeptForRetry);
