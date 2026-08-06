namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

// Mid-walk snapshotting for TreeBuilder, as extension methods (matching RuntimeIndex's BucketExtensions/
// QueryExtensions split) instead of a partial class, to keep TreeBuilder.cs under the project's line
// limit. Strictly count-based -- no wall-clock fallback, so a checkpoint only ever fires once that many
// items have genuinely been processed since the last one. The threshold-crossing/doubling mechanics
// themselves live in DoublingCheckpointGate (shared with ReFsScanner's own checkpointing); this is just
// the TreeBuilder-specific "how do I build a checkpoint store" part.
internal static class TreeBuilderCheckpointExtensions
{
    public static void MaybeCheckpoint(this TreeBuilder builder, int indexedItems)
    {
        if (builder._onCheckpoint == null)
            return;

        if (!builder._checkpointGate.TryEnter())
            return;

        try
        {
            builder._onProgress(Volatile.Read(ref builder._indexedFiles), Volatile.Read(ref builder._indexedDirs));
            builder._onCheckpoint(CloneStore(builder), CurrentStats(builder));
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
