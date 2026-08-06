using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Walk;

[TestClass]
public sealed class TreeBuilderCheckpointExtensionsTests
{
    private static TreeBuilder CreateBuilder(
        string root,
        Action<int, int>? onProgress = null,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null) => new(
        new FileRecordStore(), root, root,
        new WalkOptions([], [], [], MaxDepth: 0, WorkerCount: 1, UseIgnoreFiles: false),
        CancellationToken.None, onProgress ?? ((_, _) => { }), onCheckpoint);

    [TestMethod]
    public void MaybeCheckpoint_NoOnCheckpointCallback_NeverFiresProgressEither()
    {
        using var dir = new TempDirectory();
        var progressCalls = 0;
        var builder = CreateBuilder(dir.Path, onProgress: (_, _) => progressCalls++, onCheckpoint: null);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize + 10; i++)
            builder.MaybeCheckpoint(i);

        Assert.AreEqual(0, progressCalls);
    }

    [TestMethod]
    public void MaybeCheckpoint_BelowThreshold_DoesNotFireCheckpoint()
    {
        using var dir = new TempDirectory();
        var checkpointCalls = 0;
        var builder = CreateBuilder(dir.Path, onCheckpoint: (_, _) => checkpointCalls++);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize - 1; i++)
            builder.MaybeCheckpoint(i);

        Assert.AreEqual(0, checkpointCalls);
    }

    [TestMethod]
    public void MaybeCheckpoint_AtThreshold_FiresExactlyOnceThenResets()
    {
        using var dir = new TempDirectory();
        var checkpointCalls = 0;
        var builder = CreateBuilder(dir.Path, onCheckpoint: (_, _) => checkpointCalls++);

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            builder.MaybeCheckpoint(i);
        Assert.AreEqual(1, checkpointCalls);

        // The gap doubles after every checkpoint (see the next test), so the SECOND one needs
        // CheckpointBatchSize * 2 calls, not another CheckpointBatchSize -- one short of that still
        // shouldn't fire.
        for (var i = 0; i < TreeBuilder.CheckpointBatchSize * 2 - 1; i++)
            builder.MaybeCheckpoint(i);
        Assert.AreEqual(1, checkpointCalls);

        builder.MaybeCheckpoint(0);
        Assert.AreEqual(2, checkpointCalls);
    }

    [TestMethod]
    public void MaybeCheckpoint_EachFiring_DoublesTheGapUntilTheCap()
    {
        using var dir = new TempDirectory();
        var builder = CreateBuilder(dir.Path, onCheckpoint: (_, _) => { });

        var expectedGap = TreeBuilder.CheckpointBatchSize;
        for (var fireNumber = 1; fireNumber <= 8; fireNumber++)
        {
            Assert.AreEqual(expectedGap, builder._checkpointGate.BatchSize, $"gap before firing #{fireNumber}");
            for (var i = 0; i < expectedGap; i++)
                builder.MaybeCheckpoint(i);
            expectedGap = Math.Min(expectedGap * 2, TreeBuilder.MaxCheckpointBatchSize);
        }

        // CheckpointBatchSize (4096) doubled 6 times already exceeds MaxCheckpointBatchSize (262144),
        // so by the 7th/8th firing the gap must have stopped growing at the cap.
        Assert.AreEqual(TreeBuilder.MaxCheckpointBatchSize, builder._checkpointGate.BatchSize);
    }

    [TestMethod]
    public void MaybeCheckpoint_ClonedStore_IsIndependentOfLaterMutations()
    {
        using var dir = new TempDirectory();
        FileRecordStore? captured = null;
        var builder = CreateBuilder(dir.Path, onCheckpoint: (store, _) => captured = store);
        builder._store.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            builder.MaybeCheckpoint(i);

        Assert.IsNotNull(captured);
        Assert.HasCount(1, captured.Records);

        builder._store.Records.Add(new FileRecord(2, 1, "extra.txt", FileRecordFlags.None));

        Assert.HasCount(1, captured.Records); // the clone must not see the mutation made after checkpointing
    }

    [TestMethod]
    public void MaybeCheckpoint_PassesCurrentErrorCounterSnapshot()
    {
        using var dir = new TempDirectory();
        NetworkDriveWalkStats? captured = null;
        var builder = CreateBuilder(dir.Path, onCheckpoint: (_, stats) => captured = stats);
        builder._skippedItems = 3;
        builder._errors = 5;

        for (var i = 0; i < TreeBuilder.CheckpointBatchSize; i++)
            builder.MaybeCheckpoint(i);

        Assert.IsNotNull(captured);
        Assert.AreEqual(3, captured.Value.Skipped);
        Assert.AreEqual(5, captured.Value.Errors);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
