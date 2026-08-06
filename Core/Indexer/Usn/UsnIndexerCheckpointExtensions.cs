using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core.Indexer.Usn;

// Mid-walk checkpoint publisher for a local (non-journal) drive's rebuild -- the local-drive analogue of
// NetworkIndexerPublisher.PublishCheckpoint, wired as LocalDriveWalkBuilder's onCheckpoint callback into
// TreeBuilder. This is a hard requirement, not a nicety: IndexV2 has no pure in-memory representation
// (LiveIndex always wraps a real mmap'd file, see UsnIndexerBuildExtensions' own comment on this), so
// making a long rebuild's partial progress live-searchable means writing a checkpoint to disk the same way
// network drives already do.
internal static class UsnIndexerCheckpointExtensions
{
    // Deliberately does NOT touch Status.Drives[drive].State -- it must stay "indexing" for the whole
    // rebuild (see UsnIndexer.UpdateDriveCounts's own markReady guard, which this leaves untouched), and
    // ApplyFolderChange/ConsumeMissedFolderChangeDuringRebuild already rely on that staying true across
    // every checkpoint swap, not just the final one.
    // The NetworkDriveWalkStats TreeBuilder's onCheckpoint callback also carries is ignored on purpose --
    // DriveIndexStatus (unlike NetworkIndexStatus) has no Skipped/Errors fields to populate from it.
    public static void PublishLocalDriveCheckpoint(this UsnIndexer indexer, string cacheDir, string drive, FileRecordStore store, CancellationToken token)
    {
        // Mirrors NetworkIndexerPublisher.PublishCheckpoint's own token check at its very top -- lets a
        // Stop request kill the walk at the next checkpoint boundary, not just at TreeBuilder's own
        // per-item _token.ThrowIfCancellationRequested() calls.
        token.ThrowIfCancellationRequested();

        // Mirrors NetworkIndexerPublisher.PublishCheckpoint's own "don't regress a complete cache" guard:
        // a checkpoint is always a partial, in-progress snapshot. If what's currently live for this drive
        // is already a fully complete, trusted index -- e.g. a rebuild that reused this same drive's own
        // complete index as its TreeDiffBaseline -- persisting this mid-walk snapshot over it would
        // regress the on-disk cache and live search back to a smaller view, permanently if the walk is
        // then cancelled or the process crashes before finishing. Skip entirely; the last known-good
        // complete index keeps serving searches until the walk actually finishes and can genuinely
        // replace it. Checked BEFORE SnapshotWriter.Write below, which unconditionally overwrites this
        // drive's on-disk cache file as part of just being called.
        LiveIndex? currentBeforeSave;
        lock (indexer.LockObj)
            indexer._recordIndexes.TryGetValue(drive, out currentBeforeSave);
        try
        {
            if (currentBeforeSave != null && currentBeforeSave.IsComplete)
                return;
        }
        catch (ObjectDisposedException)
        {
            // Mirrors ApplyFolderChange's own catch for the identical race: currentBeforeSave was disposed
            // by a concurrent DropDriveFromRuntime (e.g. the drive's cache got deleted, or a catch-up
            // failure dropped it as untrustworthy) in the unlocked window between the lookup above and this
            // read. Nothing left to protect -- fall through and checkpoint normally rather than letting this
            // propagate up and fail the whole rebuild.
        }

        var path = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
        SnapshotWriter.Write(store, path);
        var live = new LiveIndex(Snapshot.Open(path));

        LiveIndex? old = null;
        var stored = false;
        try
        {
            lock (indexer.LockObj)
            {
                if (token.IsCancellationRequested || !indexer.Status.Drives.Any(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase)))
                    return;

                indexer._recordIndexes.TryGetValue(drive, out old);
                indexer._recordIndexes[drive] = live;
                stored = true;

                var totals = indexer._recordIndexes.Values.Select(r => r.GetCounts()).ToList();
                indexer.Status.TotalFiles = totals.Sum(t => t.Files);
                indexer.Status.TotalDirs = totals.Sum(t => t.Dirs);

                var item = indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    var (files, dirs) = live.GetCounts();
                    item.Files = files;
                    item.Dirs = dirs;
                    // item.State intentionally untouched -- stays "indexing".
                }
            }
        }
        finally
        {
            if (!stored)
                live.Dispose();
        }
        // Dispose OUTSIDE the lock -- LiveIndex.Dispose() can briefly block on an in-flight search's read
        // lock, and indexer.LockObj is shared across every drive, not just this one (see
        // NetworkIndexerPublisher.PublishCheckpoint's own comment for the same reasoning).
        if (old != null && !ReferenceEquals(old, live))
            old.Dispose();
        indexer.PublishStatusChanged();
    }
}
