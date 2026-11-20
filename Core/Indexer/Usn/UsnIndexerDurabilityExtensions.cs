using Lertaro.Core.IndexV2;

namespace Lertaro.Core.Indexer.Usn;

// Advances the durable USN watermark and persists snapshots during idle maintenance. UsnMonitor advances
// progress; SearchEngine's idle trim saves it. Persisting live progress bounds replay work after restart
// and reduces exposure to journal wrap.
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

    // Folds each drive's accumulated delta into its cache file, stamped with the live watermark (journal
    // drives) or the metadata it was built with (FAT32/exFAT drives, whose only in-session persist this is).
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

                // A folder (FAT32/exFAT) drive mid-rebuild: this LiveIndex is about to be replaced
                // wholesale and the rebuild's own SnapshotWriter.Write owns the cache path, so writing it
                // here would only race that. Its stamp is just the metadata it was built with -- there
                // is no live journal position to advance.
                if (!VolumeHelper.IsJournalCapableFileSystem(metadata.FileSystemType) &&
                    indexer.Status.Drives.Any(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase) && d.State == "indexing"))
                    continue;

                // Runs on SearchEngine's idle Timer, where an escaping exception ends the process: a LiveIndex
                // a rebuild disposed (it swapped this one out) is skipped, not read.
                int pending;
                try
                {
                    pending = live.PendingChangeCount;
                }
                catch (ObjectDisposedException)
                {
                    continue;
                }
                if (pending < IdleCompactPendingThreshold)
                    continue;

                // A replay owns its drive until it finishes: Compact holds the LiveIndex write lock
                // across the merge and the whole snapshot rewrite, and taking it in the middle of a
                // catch-up drain stalls exactly the replay that is trying to catch up before the journal
                // moves on. The watermark stays where it is, so the next tick after the drain persists
                // the same changes -- nothing is lost by waiting.
                if (indexer.IsReplayInProgress(drive))
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
