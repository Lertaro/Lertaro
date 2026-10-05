using System.Diagnostics;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.NetworkDrive.Scheduling;

// The actual per-drive scan pass, extracted out of Scheduler (composition, not a partial class) to keep
// that type's files under the project's line limit. Takes Scheduler's own status/checkpoint/completion
// callbacks as explicit parameters rather than reaching into Scheduler's private fields.
internal static class DriveRefreshRunner
{
    public static void RefreshDrive(
        string drive,
        CancellationToken token,
        Action<string, string, int?, string?> setStatus,
        Func<string, FileRecordStore?> getPreviousStore,
        Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> onPublishCheckpoint,
        Action<string, NetworkIndex> onRefreshFinished,
        Action<string> releaseCachedIndex)
    {
        var root = PathHelpers.BuildSourceRoot(drive);
        var physicalRoot = root;

        // A drive/share that's temporarily offline (virtual disk unmounted, network hiccup) enumerates
        // its root as empty rather than throwing loudly -- TreeBuilder.WalkDirectory just CountErrors and
        // returns, so the walk below "succeeds" with a near-empty store (just the root record), and would
        // unconditionally overwrite a good, previously-built cache with it. Bailing out here before
        // touching status/building anything leaves the existing cache and status completely untouched,
        // so it just gets picked up again next scheduled/manual refresh once the drive is back.
        if (!Directory.Exists(physicalRoot))
        {
            Logger.Log($"[NetworkIndexer] {drive}: root is currently unreachable, skipping this refresh (existing cache left untouched).", LogLevel.Warn);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            setStatus(drive, "indexing", 0, null);
            var options = WalkOptions.FromUserSettings(UserSettings.Load());
            var previousStore = getPreviousStore(drive);
            LogResumeProgress(drive, previousStore);
            var index = NetworkIndex.Build(
                drive,
                root,
                physicalRoot,
                options,
                token,
                // Fires every ~1024 items; unguarded, this can race past CancelDrive's "cached" revert
                // and clobber it back to "indexing" -- what made the Stop button lose that race sometimes.
                (files, dirs) => { if (!token.IsCancellationRequested) setStatus(drive, "indexing", files + dirs, null); },
                (store, stats) => onPublishCheckpoint(drive, store, stats, token),
                previousStore,
                beforeFinalWrite: () => releaseCachedIndex(drive));
            token.ThrowIfCancellationRequested();
            // Queue completion does not prove every directory was captured. Keep that separate fact
            // in a sidecar so the next startup retries a scan after an enumeration error.
            // ponytail: the diff walker compares live children even for Listed directories, so this
            // recovery still enumerates the tree. Targeted retries need a persisted failed-path set.
            index.IsComplete = true;
            var cachePath = IndexerHelper.GetCachePath(drive);
            var uncaptured = index.EnumerateErrors > 0;
            if (uncaptured)
            {
                Logger.Log($"[NetworkIndexer] {drive}: finished with {index.Errors} error(s) ({index.EnumerateErrors} enumerate, {index.AttributeErrors} attribute); {index.EnumerateErrors} directory listing(s) were never captured -- they stay un-Listed and are marked for another pass.", LogLevel.Warn);
                // Marker BEFORE the cache write: interrupted between the two, a stale marker only costs
                // one extra incremental pass, while a missing one would hide these directories behind
                // "complete" again.
                index.IsComplete = SetUncapturedMarker(cachePath, uncaptured: true);
            }
            else if (index.Errors > 0)
            {
                Logger.Log($"[NetworkIndexer] {drive}: finished with {index.Errors} error(s) ({index.EnumerateErrors} enumerate, {index.AttributeErrors} attribute).", LogLevel.Warn);
            }

            IndexerHelper.Save(index);

            // Cleared only AFTER the cache is on disk, for the same reason in the other direction.
            if (!uncaptured)
                SetUncapturedMarker(cachePath, uncaptured: false);

            stopwatch.Stop();
            Logger.Log($"[NetworkIndexer] {drive}: finished in {stopwatch.Elapsed.TotalSeconds:F1}s, {index.Count} records.");

            onRefreshFinished(drive, index);
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"[NetworkIndexer] {drive}: refresh cancelled, keeping the last checkpoint.");
            // A drive removed from config already had its status entry deleted (NetworkIndexer.Configure),
            // so SetStatus's own "only update an entry that still exists" guard makes this a no-op there --
            // this only actually reverts the status for a drive a user stopped via CancelDrive while it
            // remains configured, so it shows what's on disk from the last checkpoint instead of being
            // stuck on "indexing" forever.
            setStatus(drive, "cached", null, null);
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] Failed to index {drive}: {ex.Message}", LogLevel.Error);
            setStatus(drive, "error", null, ex.Message);
        }
    }

    // "Complete, but some directories were never captured" is persisted as a marker file next to the
    // drive's cache, not as a field inside the index's own snapshot: adding one to IndexV2's header would
    // change the on-disk format and invalidate every existing cache on upgrade (see SnapshotFormat.Version).
    // The name deliberately does not end in ".idx", which NetworkDriveCacheLocator.EnumerateNetworkStores
    // globs for when listing cached drives.
    internal const string UncapturedMarkerSuffix = ".uncaptured";

    internal static string GetUncapturedMarkerPath(string cachePath) => cachePath + UncapturedMarkerSuffix;

    // What tells "complete" apart from "complete, but directories are missing" -- the one fact
    // NetworkIndexer.Configure's cachedDrives gate needs.
    internal static bool HasUncapturedMarker(string cachePath) => File.Exists(GetUncapturedMarkerPath(cachePath));

    internal static bool SetUncapturedMarker(string cachePath, bool uncaptured)
    {
        var path = GetUncapturedMarkerPath(cachePath);
        try
        {
            if (uncaptured)
                File.WriteAllText(path, string.Empty);
            else
                File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            // Preserve incompleteness in the snapshot if the sidecar cannot be written.
            Logger.Log($"[NetworkIndexer] Failed to update the un-captured-directory marker {path}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    // Directories-listed ratio is a proxy for "how far the previous pass got": a directory only carries
    // FileRecordFlags.Listed once its own children were fully captured, so this is what TreeDiffBaseline
    // will actually be able to trust and skip re-listing, as opposed to just the raw record count.
    private static void LogResumeProgress(string drive, FileRecordStore? previousStore)
    {
        if (previousStore == null)
        {
            Logger.Log($"[NetworkIndexer] {drive}: no previous index to resume from, starting a fresh scan.");
            return;
        }

        var totalDirs = 0;
        var listedDirs = 0;
        foreach (var record in previousStore.Records)
        {
            if (!record.IsDirectory)
                continue;
            totalDirs++;
            if ((record.Flags & FileRecordFlags.Listed) != 0)
                listedDirs++;
        }

        Logger.Log($"[NetworkIndexer] {drive}: resuming with {previousStore.Records.Count} records from last pass " +
            $"({listedDirs}/{totalDirs} directories confirmed listed, previous IsComplete={previousStore.IsComplete}).");
    }
}
