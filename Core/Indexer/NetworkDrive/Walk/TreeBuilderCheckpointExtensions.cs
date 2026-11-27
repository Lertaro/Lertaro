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
// The check and the callback sit under the read side of builder._callbackGate and the abandon under its
// write side, so no callback can pass the check and then run after the abandon.
internal static class TreeBuilderCheckpointExtensions
{
    public static void MaybeReportProgress(this TreeBuilder builder)
    {
        if (Interlocked.Increment(ref builder._countSinceProgress) < TreeBuilder.ProgressBatchSize)
            return;

        Interlocked.Exchange(ref builder._countSinceProgress, 0);
        builder.UnlessAbandoned(() => builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs)));
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
            builder.UnlessAbandoned(() =>
            {
                builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
                try
                {
                    builder._onCheckpoint(snapshot, CurrentStats(builder));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A checkpoint is a resume point, not the walk's result: a failed write (a full disk, a
                    // locked or read-only cache file) leaves the store being built untouched, so the walk goes
                    // on. What else it costs is the publisher's: PublishLocalDriveCheckpoint fails in
                    // SnapshotWriter.Write before touching the runtime, so the previous live index keeps
                    // serving. NetworkIndexerPublisher.PublishCheckpoint catches its own failures, but it has
                    // already released the drive's cached index by then, so that drive serves no index until a
                    // later checkpoint or the final save succeeds (that predates this catch). Anything else a
                    // checkpoint throws is a bug and propagates, and so does cancellation, which the
                    // publishers throw to stop the walk at a checkpoint boundary.
                    Logger.Log($"[NetworkIndexer] A mid-walk checkpoint failed; the walk continues without it: {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
                }
            });
        }
        finally
        {
            builder._checkpointGate.Completed();
        }
    }

    private static void UnlessAbandoned(this TreeBuilder builder, Action callback)
    {
        builder._callbackGate.EnterReadLock();
        try
        {
            if (!builder._token.IsCancellationRequested)
                callback();
        }
        finally
        {
            builder._callbackGate.ExitReadLock();
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
