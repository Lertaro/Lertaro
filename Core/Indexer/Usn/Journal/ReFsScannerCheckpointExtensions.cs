using System.Collections.Concurrent;
using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Indexer.Usn.Journal;

// Publishes ReFsScanner checkpoints during a walk. CheckpointState shares
// drive/rootFrn/nextUsn/journalId, the callback, and a DoublingCheckpointGate across workers, using
// TreeBuilder's checkpoint thresholds.
internal sealed class ReFsCheckpointState
{
    public readonly string Drive;
    public readonly UInt128 RootFrn;
    public readonly long NextUsn;
    public readonly ulong JournalId;
    public readonly Action<FileRecordStore, NetworkDriveWalkStats>? OnCheckpoint;
    public readonly DoublingCheckpointGate Gate = new(TreeBuilder.CheckpointBatchSize, TreeBuilder.MaxCheckpointBatchSize);

    public ReFsCheckpointState(string drive, UInt128 rootFrn, long nextUsn, ulong journalId, Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint)
    {
        Drive = drive;
        RootFrn = rootFrn;
        NextUsn = nextUsn;
        JournalId = journalId;
        OnCheckpoint = onCheckpoint;
    }
}

internal static class ReFsScannerCheckpointExtensions
{
    public static void MaybeCheckpoint(this ReFsCheckpointState? state, ConcurrentDictionary<UInt128, ReFsItem> items)
    {
        if (state?.OnCheckpoint == null)
            return;

        if (!state.Gate.TryEnter())
            return;

        try
        {
            // Point-in-time copy -- ConcurrentDictionary's own enumeration is thread-safe against
            // concurrent writes from other workers, but IndexCacheManager.CreateStoreFromDriveData needs a
            // plain Dictionary; a checkpoint's contents are inherently a partial, ephemeral snapshot
            // regardless of exactly which in-flight writes did or didn't make it in.
            var snapshot = new Dictionary<UInt128, ReFsItem>(items);
            var store = IndexCacheManager.CreateStoreFromDriveData(state.Drive, state.RootFrn, snapshot, state.NextUsn, state.JournalId);
            state.OnCheckpoint(store, default);
        }
        finally
        {
            state.Gate.Completed();
        }
    }
}
