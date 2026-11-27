using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

// PublishLocalDriveCheckpoint's own disk write (SnapshotWriter.Write/Snapshot.Open) is real -- these tests
// use a real temp cache directory for that, but otherwise mirror NetworkIndexerPublisherTests' style for
// the in-memory swap/status bookkeeping.
[TestClass]
public sealed class UsnIndexerCheckpointExtensionsTests
{
    [TestMethod]
    public void PublishLocalDriveCheckpoint_DriveTracked_SwapsInNewLiveIndexAndKeepsStateIndexing()
    {
        using var cacheDir = new TempDirectory();
        // Not `using` -- PublishLocalDriveCheckpoint disposes oldFixture.Index itself as part of the swap;
        // see the next test's own comment on why LiveIndex.Dispose() can't be called a second time.
        var oldFixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        try
        {
            var indexer = new UsnIndexer();
            indexer._recordIndexes["C"] = oldFixture.Index;
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

            var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
            checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            checkpointStore.Records.Add(new FileRecord(2, 1, "file.txt", FileRecordFlags.None));

            indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

            Assert.AreNotSame(oldFixture.Index, indexer._recordIndexes["C"]);
            var item = indexer.Status.Drives.Single(d => d.Drive == "C");
            Assert.AreEqual("indexing", item.State);
            Assert.AreEqual(1, item.Files);
            indexer._recordIndexes["C"].Dispose();
        }
        finally
        {
            try { oldFixture.Dispose(); } catch { }
        }
    }

    [TestMethod]
    public void PublishLocalDriveCheckpoint_DriveTracked_DisposesThePreviousLiveIndex()
    {
        using var cacheDir = new TempDirectory();
        // Not `using` -- PublishLocalDriveCheckpoint disposes oldFixture.Index itself below, and
        // LiveIndex.Dispose() isn't safe to call twice (see this session's earlier ApplyFolderChange
        // regression tests for the same reasoning), so cleanup is done manually in the finally block.
        var oldFixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        try
        {
            var indexer = new UsnIndexer();
            indexer._recordIndexes["C"] = oldFixture.Index;
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

            var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
            checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

            indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

            // The old LiveIndex is disposed once swapped out -- GetCounts touching its (now-disposed)
            // internal lock should throw, same as the ApplyFolderChange/SaveDriveSnapshot disposed-instance
            // regressions covered earlier this session.
            Assert.ThrowsExactly<ObjectDisposedException>(() => oldFixture.Index.GetCounts());
        }
        finally
        {
            try { oldFixture.Dispose(); } catch { }
        }
    }

    // Regression coverage for the "don't regress a complete cache" guard, mirroring
    // NetworkIndexerPublisherTests' equivalent PublishCheckpoint coverage: a mid-walk checkpoint must
    // never overwrite an already-complete cached index with a smaller partial one.
    [TestMethod]
    public void PublishLocalDriveCheckpoint_ExistingIndexAlreadyComplete_SkipsSwapWithoutRegressingIt()
    {
        using var cacheDir = new TempDirectory();
        using var oldFixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() }, isComplete: true);
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = oldFixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

        var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
        checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

        Assert.AreSame(oldFixture.Index, indexer._recordIndexes["C"]);
        // The old index must not have been disposed either -- still fully usable.
        var (files, _) = oldFixture.Index.GetCounts();
        Assert.AreEqual(0, files);
    }

    // Regression coverage: the "don't regress a complete cache" guard above reads currentBeforeSave.IsComplete
    // outside the lock that looked it up -- a concurrent DropDriveFromRuntime (e.g. the user deletes this
    // drive's cache, or a catch-up failure drops it as untrustworthy) disposing that exact LiveIndex in that
    // window used to propagate ObjectDisposedException out of this method and fail the whole rebuild, unlike
    // every other disposed-instance race in this file, which falls through gracefully.
    [TestMethod]
    public void PublishLocalDriveCheckpoint_ExistingIndexDisposedConcurrently_FallsThroughInsteadOfThrowing()
    {
        using var cacheDir = new TempDirectory();
        var oldFixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() }, isComplete: true);
        try
        {
            var indexer = new UsnIndexer();
            indexer._recordIndexes["C"] = oldFixture.Index;
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

            // Simulates DropDriveFromRuntime running in the unlocked window between
            // PublishLocalDriveCheckpoint's lookup and its IsComplete read -- it removes AND disposes this
            // exact instance together under its own lock, same as the real method does, so `old` below
            // finds nothing left to double-dispose.
            indexer._recordIndexes.Remove("C");
            oldFixture.Index.Dispose();

            var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
            checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

            indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

            // Fell through and checkpointed normally instead of throwing.
            Assert.AreNotSame(oldFixture.Index, indexer._recordIndexes["C"]);
            indexer._recordIndexes["C"].Dispose();
        }
        finally
        {
            try { oldFixture.Dispose(); } catch { }
        }
    }

    [TestMethod]
    public void PublishLocalDriveCheckpoint_DriveNotTracked_DisposesTheNewIndexWithoutThrowing()
    {
        using var cacheDir = new TempDirectory();
        var indexer = new UsnIndexer();
        // No Status.Drives entry for "C" -- mirrors the drive having been removed/disabled mid-checkpoint.
        var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
        checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

        Assert.IsFalse(indexer._recordIndexes.ContainsKey("C"));
    }

    [TestMethod]
    public void PublishLocalDriveCheckpoint_CancelledToken_ThrowsAndLeavesRuntimeStateUntouched()
    {
        using var cacheDir = new TempDirectory();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var indexer = new UsnIndexer();
        var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
        checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, cts.Token));

        Assert.IsFalse(indexer._recordIndexes.ContainsKey("C"));
    }

    // Regression coverage mirroring this session's earlier ApplyFolderChange/SaveDriveSnapshot fixes: a
    // watcher callback that already looked up the drive's LiveIndex before a checkpoint swap disposes it
    // must not crash -- ApplyFolderChange's existing catch (ObjectDisposedException) already covers this,
    // since it's the same disposed-instance race regardless of whether OnDriveCompleted or a mid-walk
    // checkpoint is what did the disposing.
    [TestMethod]
    public void PublishLocalDriveCheckpoint_ConcurrentApplyFolderChangeAgainstThePreSwapInstance_FlagsMissedInsteadOfThrowing()
    {
        using var cacheDir = new TempDirectory();
        // Not `using` -- disposed via the checkpoint swap below, then re-disposed by the finally block;
        // see the earlier test's own comment on why LiveIndex.Dispose() can't be called twice.
        var oldFixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        try
        {
            var indexer = new UsnIndexer();
            indexer._recordIndexes["C"] = oldFixture.Index;
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

            var checkpointStore = new FileRecordStore { SourceKey = "C", SourceKind = FileRecordSourceKind.LocalMft, IdKind = FileRecordIdKind.SourceLocalId64, RootId = 1 };
            checkpointStore.Records.Add(new FileRecord(1, 1, "", FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
            indexer.PublishLocalDriveCheckpoint(cacheDir.Path, "C", checkpointStore, CancellationToken.None);

            // Simulates a watcher callback that captured oldFixture.Index (via TryGetValue) an instant
            // BEFORE the checkpoint above disposed and swapped it out -- puts that now-disposed instance
            // back where ApplyFolderChange's own lookup will find it, reproducing the exact race shape.
            indexer._recordIndexes["C"] = oldFixture.Index;

            indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, @"C:\somewhere\new-file.txt");

            Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        }
        finally
        {
            try { oldFixture.Dispose(); } catch { }
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
