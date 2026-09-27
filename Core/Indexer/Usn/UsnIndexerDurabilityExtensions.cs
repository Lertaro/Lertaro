using Lertaro.Core.IndexV2;

namespace Lertaro.Core.Indexer.Usn;

// What makes a journal drive's live progress survive a restart: the durable watermark (the position a
// snapshot is stamped with, hence where the next cold start replays FROM) and the idle-time persist that
// gives that watermark anything to be worth. Split out of UsnIndexerExtensions for the repo's per-file
// line limit; everything here operates on state UsnIndexer owns and is called from UsnMonitor (advance)
// and SearchEngine's idle trim (persist).
//
// Without the persist half, a USN drive's cache file and watermark sat frozen at the last cold-start
// catch-up for the entire session: every restart replayed the whole journal range since then (bounded by
// the volume's own journal window, 32 MB by default on a system drive that can wrap in hours), and any
// batch that failed to land stayed invisible until one of those restarts.
internal static class UsnIndexerDurabilityExtensions
{
    // Pending delta changes required before an idle tick is worth rewriting the whole snapshot for.
    // Self-throttling rather than timed: Compact swaps in a fresh DeltaOverlay, so this gate closes the
    // moment a write succeeds and only reopens once this much NEW churn has accumulated.
    internal const int IdleCompactPendingThreshold = 4096;

    // Records the journal position a successfully applied batch reached, so the next persist can stamp
    // it. Only ever called for a batch ApplyUsnRecords said it applied, and never past a pin, which is
    // what keeps "watermark <= what the snapshot actually contains" true.
    public static void AdvanceJournalWatermark(this UsnIndexer indexer, string drive, ulong journalId, long nextUsn)
    {
        lock (indexer.LockObj)
        {
            if (!indexer._driveMetadata.TryGetValue(drive, out var metadata))
                return;

            if (metadata.JournalWatermarkPinned)
                return;

            if (metadata.JournalId != journalId)
            {
                // The journal was recreated under a still-running monitor. Nothing newer can be
                // replayed from here, so pin at the last position this instance was consistent with.
                metadata.JournalWatermarkPinned = true;
                Logger.Log($"[UsnIndexer] {drive}: journal changed from {metadata.JournalId} to {journalId} mid-session; watermark pinned at {metadata.NextUsn} so the next cold start replays from there.", LogLevel.Warn);
                return;
            }

            metadata.JournalId = journalId;
            metadata.NextUsn = nextUsn;
        }
    }

    // Stops the watermark for a drive's CURRENT metadata instance, called when a batch had nowhere to
    // land. Cold start is the recovery path for a pin (catch-up replays from the persisted stamp), and a
    // rebuild clears it by installing a new instance.
    public static void PinJournalWatermark(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            if (indexer._driveMetadata.TryGetValue(drive, out var metadata))
                metadata.JournalWatermarkPinned = true;
    }

    public static bool IsJournalWatermarkPinned(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            return indexer._driveMetadata.TryGetValue(drive, out var metadata) && metadata.JournalWatermarkPinned;
    }

    // Folds each journal drive's accumulated delta into its cache file, stamped with the live watermark.
    // Returns how many drives were written. Cheap to call often: drives below IdleCompactPendingThreshold
    // are skipped outright and Compact's own force:false path skips one whose delta emptied meanwhile.
    public static int CompactIdleDeltas(this UsnIndexer indexer, string cacheDir)
    {
        var targets = new List<(string Drive, LiveIndex Live, CompactionStamp Stamp)>();
        lock (indexer.LockObj)
        {
            foreach (var (drive, live) in indexer._recordIndexes)
            {
                if (!indexer._driveMetadata.TryGetValue(drive, out var metadata))
                    continue;

                // Journal drives only. A folder/watcher drive already debounces its own persist from
                // ApplyFolderChange, and its metadata carries no live journal position to stamp with.
                if (!VolumeHelper.IsJournalCapableFileSystem(metadata.FileSystemType))
                    continue;

                if (live.PendingChangeCount < IdleCompactPendingThreshold)
                    continue;

                // Stamp read BEFORE Compact, never after: a batch landing in between can only make the
                // persisted content NEWER than the stamp, so the next cold start replays a few records
                // that were already applied (harmless, and RemoveLink reports them). The opposite order
                // could persist a stamp ahead of content, which is the permanent hole this pins against.
                targets.Add((drive, live, new CompactionStamp(metadata.JournalId, metadata.NextUsn)));
            }
        }

        var written = 0;
        foreach (var (drive, live, stamp) in targets)
        {
            try
            {
                // Outside LockObj on purpose: this holds the LiveIndex write lock across a full merge and
                // a multi-hundred-MB rewrite, which no search should queue behind while the indexer is
                // otherwise idle. Compact's own force:false gate re-checks the delta under that lock.
                if (live.Compact(LocalDriveCacheLocator.GetCachePath(cacheDir, drive), stamp))
                    written++;
            }
            catch (ObjectDisposedException)
            {
                // A rebuild swapped this drive's LiveIndex out mid-write; its own SnapshotWriter.Write
                // owns the cache path now, so there is nothing left for this persist to say.
            }
            catch (Exception ex)
            {
                Logger.Log($"[UsnIndexer] {drive}: idle compaction failed: {ex.Message}", LogLevel.Error);
            }
        }

        return written;
    }
}
