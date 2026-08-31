using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2;

// Owns one drive's Snapshot + DeltaOverlay pair and the concurrency model the prototype never needed
// (a single-threaded benchmark has no readers to race). Snapshot itself is immutable once mapped, so
// searches never need to block each other -- only DeltaOverlay's mutable dictionaries/lists need
// protection, and Compact() needs a consistent view to merge from. Readers take the read lock (many
// concurrent searches); a USN/watcher batch or a Compact()/SwapSnapshot() call takes the write lock.
//
// Compact() deliberately holds the write lock for its ENTIRE duration (merge + file write + reopen),
// not just the final swap -- DeltaOverlay's dictionaries aren't safe to iterate while a concurrent
// Mutate() call could be appending to them, so there is no cheaper correct alternative without a
// versioned/copy-on-write overlay. This blocks searches for the duration of one compaction (bounded,
// background/idle-triggered), matching this codebase's existing tolerance for blocking full-process
// GC.Collect calls during rebuilds. Shrinking this window is a valid future optimization, not a
// correctness requirement.
public sealed class LiveIndex : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private Snapshot? _snapshot;
    private DeltaOverlay? _delta;
    private long _revision;

    public LiveIndex(Snapshot snapshot)
    {
        _snapshot = snapshot;
        _delta = new DeltaOverlay(snapshot);
    }

    public string SourceKey => _snapshot?.SourceKey ?? throw new ObjectDisposedException(nameof(LiveIndex));

    private void EnsureUsable()
    {
        if (_snapshot == null)
            throw new ObjectDisposedException(nameof(LiveIndex));
    }
    public long Revision => Interlocked.Read(ref _revision);

    // The scan-completeness marker recorded when this snapshot was written. DeltaOverlay never changes
    // it, so this reads straight off the base snapshot rather than merging through ToStore().
    public bool IsComplete => Read((snapshot, _) => snapshot.IsComplete);

    // Runs `read` under the read lock with a consistent (Snapshot, DeltaOverlay) pair -- multiple
    // searches run concurrently, but never overlap a mutation or a compaction swap.
    public T Read<T>(Func<Snapshot, DeltaOverlay, T> read)
    {
        _lock.EnterReadLock();
        try
        {
            EnsureUsable();
            return read(_snapshot!, _delta!);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Runs `mutate` under the write lock -- USN/watcher batches call in here so their whole batch is
    // atomic with respect to concurrent searches (a search never sees half an update batch applied).
    public void Mutate(Action<Snapshot, DeltaOverlay> mutate)
    {
        _lock.EnterWriteLock();
        try
        {
            EnsureUsable();
            mutate(_snapshot!, _delta!);
            Interlocked.Increment(ref _revision);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public int PendingChangeCount => Read((_, delta) => delta.PendingChangeCount);

    // Current live state (base+delta merged) as a FileRecordStore -- e.g. for a network/WSL/folder
    // drive's next scan pass to use as its TreeDiffBaseline input (NetworkIndex.ToStore()).
    public FileRecordStore ToStore() => Read((snapshot, delta) => Compaction.BuildMergedStore(snapshot, delta));

    // Live totals = the base snapshot's frozen counts plus every visibility change the overlay has
    // made since -- O(1), safe to poll frequently (status bar, etc.) without rescanning the drive.
    public (int Files, int Dirs) GetCounts() => Read((snapshot, delta) =>
        (Math.Max(0, snapshot.TotalFiles + delta.FileCountDelta), Math.Max(0, snapshot.TotalDirs + delta.DirCountDelta)));

    // Folds the current Snapshot+DeltaOverlay into a fresh snapshot file at `path` and swaps it in.
    // No-op (returns false) unless `force` or there is something pending to fold -- a periodic
    // idle-triggered compactor should pass force:false to skip pointless rewrites; a caller that must
    // durably persist a new watermark regardless of file-level churn (matching the old engine's
    // SaveDrivesToCache, which always wrote) should pass force:true. `stamp` lets a caller override
    // fields the snapshot itself isn't the live authority for -- JournalId/NextUsn for local drives,
    // IsComplete/ExclusionRulesFingerprint/LastUpdated for network/WSL/folder drives; omitted fields
    // keep the snapshot's current value.
    public bool Compact(string path, CompactionStamp stamp = default, bool force = false)
    {
        _lock.EnterWriteLock();
        try
        {
            EnsureUsable();
            if (!force && _delta!.PendingChangeCount == 0)
                return false;

            // Merge first (the only step that still needs the OLD snapshot's memory-mapped data), then
            // release that mapping BEFORE writing the fresh file over this same path. SnapshotWriter's
            // temp-then-replace swap needs to delete the resulting backup file right after the rename,
            // and an active memory mapping on it -- even one opened with FileShare.Delete, which only
            // guarantees the RENAME succeeds -- can make that immediately-following delete fail on some
            // Windows/filesystem combinations (seen reliably on a Windows 10 VM indexing a network share,
            // not on Windows 11), leaving an orphaned .bak file this exact process still holds open.
            var mergedStore = Compaction.BuildMergedStore(_snapshot!, _delta!, stamp);
            _snapshot!.Dispose();
            _snapshot = null;
            _delta = null;
            try
            {
                SnapshotWriter.Write(mergedStore, path);
                _snapshot = Snapshot.Open(path);
                _delta = new DeltaOverlay(_snapshot);
                Interlocked.Increment(ref _revision);
                return true;
            }
            catch (Exception originalException)
            {
                // SnapshotWriter.Write uses File.Replace, which is all-or-nothing. On a write failure the
                // original pre-compaction file should still be at `path`; on a successful write followed by
                // an open failure the fresh file is there but unreadable. Either way, try to keep this
                // LiveIndex serving a valid snapshot before propagating the failure.
                try
                {
                    _snapshot = Snapshot.Open(path);
                    _delta = new DeltaOverlay(_snapshot);
                    Logger.Log($"LiveIndex.Compact failed to write/reopen '{path}', recovered the on-disk snapshot: {originalException.Message}", LogLevel.Error);
                }
                catch (Exception reopenException)
                {
                    Logger.Log($"LiveIndex.Compact could not recover a snapshot after '{path}' write/reopen failure: {reopenException.Message}", LogLevel.Error);
                    _snapshot = null;
                    _delta = null;
                    throw new ObjectDisposedException(nameof(LiveIndex), originalException);
                }

                throw;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Swaps in a snapshot built OUTSIDE this LiveIndex (e.g. a full rebuild's first load) and resets
    // the overlay to empty. Compact() reaches the same end state via SwapUnderLock while already
    // holding the write lock; this is the entry point for callers that aren't already inside one.
    public void SwapSnapshot(Snapshot newSnapshot)
    {
        _lock.EnterWriteLock();
        try
        {
            EnsureUsable();
            SwapUnderLock(newSnapshot);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Caller must already hold the write lock. Safe to dispose the old mapping synchronously here:
    // the write lock is exclusive, so no Read() can be holding a reference into it concurrently.
    private void SwapUnderLock(Snapshot newSnapshot)
    {
        var old = _snapshot;
        _snapshot = newSnapshot;
        _delta = new DeltaOverlay(newSnapshot);
        Interlocked.Increment(ref _revision);
        old?.Dispose();
    }

    public void Dispose()
    {
        _lock.EnterWriteLock();
        try
        {
            _snapshot?.Dispose();
            _snapshot = null;
            _delta = null;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        _lock.Dispose();
    }
}
