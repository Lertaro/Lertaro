using System.Collections.Concurrent;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.Indexer.Usn.Journal;

namespace Lertaro.Core.Tests.Indexer.Usn.Journal;

[TestClass]
public sealed class ReFsScannerCheckpointExtensionsTests
{
    private static readonly ConcurrentDictionary<UInt128, ReFsItem> EmptyItems = new();

    private static ReFsCheckpointState CreateState(Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null) =>
        new("~", rootFrn: 1, nextUsn: 0, journalId: 0, onCheckpoint);

    [TestMethod]
    public void MaybeCheckpoint_NullState_DoesNotThrow()
    {
        ReFsCheckpointState? state = null;
        state.MaybeCheckpoint(EmptyItems);
    }

    [TestMethod]
    public void MaybeCheckpoint_NoOnCheckpointCallback_NeverFires()
    {
        var state = CreateState(onCheckpoint: null);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize + 10; i++)
            state.MaybeCheckpoint(EmptyItems);

        Assert.AreEqual(TreeBuilder.CheckpointBatchSize, state.Gate.BatchSize); // never even started counting -- bails before TryEnter runs
    }

    [TestMethod]
    public void MaybeCheckpoint_BelowThreshold_DoesNotFireCheckpoint()
    {
        var checkpointCalls = 0;
        var state = CreateState(onCheckpoint: (_, _) => checkpointCalls++);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize - 1; i++)
            state.MaybeCheckpoint(EmptyItems);

        Assert.AreEqual(0, checkpointCalls);
    }

    [TestMethod]
    public void MaybeCheckpoint_AtThreshold_FiresExactlyOnceThenResets()
    {
        var checkpointCalls = 0;
        var state = CreateState(onCheckpoint: (_, _) => checkpointCalls++);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            state.MaybeCheckpoint(EmptyItems);
        Assert.AreEqual(1, checkpointCalls);

        // The gap doubles after every checkpoint (see the next test), so the SECOND one needs
        // CheckpointBatchSize * 2 calls, not another CheckpointBatchSize -- one short of that still
        // shouldn't fire.
        for (var i = 0; i < TreeBuilder.CheckpointBatchSize * 2 - 1; i++)
            state.MaybeCheckpoint(EmptyItems);
        Assert.AreEqual(1, checkpointCalls);

        state.MaybeCheckpoint(EmptyItems);
        Assert.AreEqual(2, checkpointCalls);
    }

    [TestMethod]
    public void MaybeCheckpoint_EachFiring_DoublesTheGapUntilTheCap()
    {
        var state = CreateState(onCheckpoint: (_, _) => { });

        var expectedGap = TreeBuilder.CheckpointBatchSize;
        for (var fireNumber = 1; fireNumber <= 8; fireNumber++)
        {
            Assert.AreEqual(expectedGap, state.Gate.BatchSize, $"gap before firing #{fireNumber}");
            for (var i = 0; i < expectedGap; i++)
                state.MaybeCheckpoint(EmptyItems);
            expectedGap = Math.Min(expectedGap * 2, TreeBuilder.MaxCheckpointBatchSize);
        }

        Assert.AreEqual(TreeBuilder.MaxCheckpointBatchSize, state.Gate.BatchSize);
    }

    [TestMethod]
    public void MaybeCheckpoint_SnapshotIsIndependentOfLaterMutations()
    {
        FileRecordStore? captured = null;
        var state = CreateState(onCheckpoint: (store, _) => captured = store);
        var items = new ConcurrentDictionary<UInt128, ReFsItem>();
        items.TryAdd(10, new ReFsItem("file.txt", 1, false, 0, 0, 0, 0));

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            state.MaybeCheckpoint(items);

        Assert.IsNotNull(captured);
        // 2 = the synthesized root record (CreateEmptyStore/CreateStoreFromDriveData always add it) + the
        // one item present in `items` at checkpoint time.
        Assert.HasCount(2, captured.Records);

        items.TryAdd(11, new ReFsItem("added-after-checkpoint.txt", 1, false, 0, 0, 0, 0));

        Assert.HasCount(2, captured.Records); // the checkpoint's own copy must not see the later addition
    }

    [TestMethod]
    public void MaybeCheckpoint_CapturedStore_CarriesTheDriveIdentityFields()
    {
        FileRecordStore? captured = null;
        var state = new ReFsCheckpointState("~", rootFrn: 42, nextUsn: 7, journalId: 9, (store, _) => captured = store);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            state.MaybeCheckpoint(EmptyItems);

        Assert.IsNotNull(captured);
        Assert.AreEqual("~", captured.SourceKey);
        Assert.AreEqual((UInt128)42, captured.RootId);
        Assert.AreEqual(7, captured.NextUsn);
        Assert.AreEqual(9UL, captured.JournalId);
    }
}
