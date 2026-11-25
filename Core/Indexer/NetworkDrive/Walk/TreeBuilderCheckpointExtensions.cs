namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Mid-walk snapshotting and progress reporting for TreeBuilder, as extension methods instead of a
// partial class, to keep TreeBuilder.cs under the project's line limit. Strictly count-based -- no
// wall-clock fallback, so a checkpoint only ever fires once that many items have genuinely been
// processed since the last one. The threshold-crossing/doubling mechanics
// themselves live in DoublingCheckpointGate (shared with ReFsScanner's own checkpointing); this is just
// the TreeBuilder-specific "how do I build a checkpoint store" part.
//
// Neither callback runs once builder._token is cancelled: AbandonIfRootLost cancels it from another
// worker while this one is still mid-directory, and the callers only guard on THEIR token, so a straggler
// would otherwise publish a checkpoint or flip the status back to "indexing" after the walk was abandoned.
// ponytail: check-then-act -- a worker that passes the check just before _abort fires still runs that one
// callback; the window is one callback invocation wide. Closing it means serialising callbacks with
// AbandonIfRootLost under one lock (or callers checking the walk's own token inside the callback).
internal static class TreeBuilderCheckpointExtensions
{
    public static void MaybeReportProgress(this TreeBuilder builder)
    {
        if (Interlocked.Increment(ref builder._countSinceProgress) < TreeBuilder.ProgressBatchSize)
            return;

        Interlocked.Exchange(ref builder._countSinceProgress, 0);
        if (!builder._token.IsCancellationRequested)
            builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
    }

    public static void MaybeCheckpoint(this TreeBuilder builder, int indexedItems)
    {
        if (builder._onCheckpoint == null)
            return;

        if (!builder._checkpointGate.TryEnter())
            return;

        try
        {
            // Cloned first so the cancellation check sits after the slow part, right before publishing.
            var snapshot = CloneStore(builder);
            if (builder._token.IsCancellationRequested)
                return;

            builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
            builder._onCheckpoint(snapshot, CurrentStats(builder));
        }
        finally
        {
            builder._checkpointGate.Completed();
        }
    }

    private static FileRecordStore CloneStore(TreeBuilder builder)
    {
        lock (builder._recordsGate)
        {
            var clone = new FileRecordStore
            {
                SourceKey = builder._store.SourceKey,
                SourceKind = builder._store.SourceKind,
                IdKind = builder._store.IdKind,
                RootId = builder._store.RootId,
                JournalId = builder._store.JournalId,
                NextUsn = builder._store.NextUsn,
                // Without this, a checkpoint saved mid-walk would look fingerprint-less on the next resume,
                // forcing an unnecessary (but harmless) recheck pass instead of correctly recognizing that
                // exclusion rules haven't actually changed since this in-progress scan started.
                ExclusionRulesFingerprint = builder._store.ExclusionRulesFingerprint
            };
            clone.Records.AddRange(builder._store.Records);
            return clone;
        }
    }

    private static NetworkDriveWalkStats CurrentStats(TreeBuilder builder) => new NetworkDriveWalkStats(
            Volatile.Read(ref builder._skippedItems),
            Volatile.Read(ref builder._errors),
            Volatile.Read(ref builder._enumerateErrors),
            Volatile.Read(ref builder._attributeErrors),
            Volatile.Read(ref builder._reparseSkipped),
            Volatile.Read(ref builder._slowDirectories));
}
