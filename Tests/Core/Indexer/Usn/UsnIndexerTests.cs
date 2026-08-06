using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

[TestClass]
public sealed class UsnIndexerTests
{
    // Regression coverage for the item-count flicker: a drive's own USN/folder monitor stays alive for
    // the drive's ENTIRE rebuild (nothing stops it beforehand -- see EnsureDriveMonitor), and
    // _recordIndexes[drive] still points at the OLD index for that whole window (BuildDrives only swaps
    // in the fresh one at completion). A stray journal/folder-change notification arriving mid-rebuild
    // must not stomp the in-progress scan's own reported Files/Dirs with the old index's totals, or flip
    // the row back to "ready" early.
    [TestMethod]
    public void UpdateDriveCounts_DriveCurrentlyIndexingWithoutMarkReady_LeavesProgressUntouched()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "old-file.txt", FileRecordFlags.None),
        });

        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing", Files = 500, Dirs = 50 });

        indexer.UpdateDriveCounts("C");

        var item = indexer.Status.Drives.Single(d => d.Drive == "C");
        Assert.AreEqual("indexing", item.State);
        Assert.AreEqual(500, item.Files);
        Assert.AreEqual(50, item.Dirs);
    }

    [TestMethod]
    public void UpdateDriveCounts_MarkReadyTrue_UpdatesCountsAndStateEvenWhileIndexing()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "new-file.txt", FileRecordFlags.None),
        });

        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing", Files = 500, Dirs = 50 });

        indexer.UpdateDriveCounts("C", markReady: true);

        var item = indexer.Status.Drives.Single(d => d.Drive == "C");
        Assert.AreEqual("ready", item.State);
        Assert.AreEqual(1, item.Files);
        Assert.AreEqual(1, item.Dirs);
    }

    [TestMethod]
    public void UpdateDriveCounts_DriveNotIndexing_UpdatesNormallyWithoutMarkReady()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "file.txt", FileRecordFlags.None),
        });

        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "unknown", Files = 0, Dirs = 0 });

        indexer.UpdateDriveCounts("C");

        var item = indexer.Status.Drives.Single(d => d.Drive == "C");
        Assert.AreEqual("ready", item.State);
        Assert.AreEqual(1, item.Files);
        Assert.AreEqual(1, item.Dirs);
    }

    // Regression coverage for the leaked-monitor bug: every call site that (re)starts a drive's monitor
    // (cold start, hot-plug recovery) now routes through RegisterDriveMonitor, which must stop whatever
    // was previously registered for that drive rather than leaving it running alongside the new one.
    [TestMethod]
    public void RegisterDriveMonitor_ReplacingAnExistingEntry_DisposesThePreviousOne()
    {
        var indexer = new UsnIndexer();
        var first = new DisposableSpy();
        var second = new DisposableSpy();

        indexer.RegisterDriveMonitor("C", first);
        Assert.IsFalse(first.WasDisposed);

        indexer.RegisterDriveMonitor("C", second);

        Assert.IsTrue(first.WasDisposed);
        Assert.IsFalse(second.WasDisposed);
    }

    [TestMethod]
    public void DisposeAllDriveMonitors_DisposesEveryRegisteredDriveAndClearsTheRegistry()
    {
        var indexer = new UsnIndexer();
        var driveC = new DisposableSpy();
        var driveD = new DisposableSpy();
        indexer.RegisterDriveMonitor("C", driveC);
        indexer.RegisterDriveMonitor("D", driveD);

        indexer.DisposeAllDriveMonitors();

        Assert.IsTrue(driveC.WasDisposed);
        Assert.IsTrue(driveD.WasDisposed);

        // The registry must be cleared, not just its contents disposed -- otherwise a later
        // RegisterDriveMonitor for "C" would try to dispose an already-disposed stale entry again.
        var replacement = new DisposableSpy();
        indexer.RegisterDriveMonitor("C", replacement);
        Assert.IsFalse(replacement.WasDisposed);
    }

    // Regression coverage: a journal-backed drive's manual rebuild stops its own monitor first (see
    // SearchEngineDriveMaintenance.ForceRebuildDrive / DriveRecovery.RestoreOrRebuild, both gated on
    // VolumeHelper.SupportsUsnJournal) so its UsnMonitor can't call ApplyUsnRecords against the old
    // LiveIndex in the narrow window OnDriveCompleted disposes it in.
    [TestMethod]
    public void RemoveDriveMonitor_ExistingEntry_DisposesItAndClearsTheRegistry()
    {
        var indexer = new UsnIndexer();
        var monitor = new DisposableSpy();
        indexer.RegisterDriveMonitor("C", monitor);

        indexer.RemoveDriveMonitor("C");

        Assert.IsTrue(monitor.WasDisposed);

        // The registry must be cleared, not just the entry disposed -- otherwise a later RegisterDriveMonitor
        // for "C" would try to dispose an already-disposed stale entry again.
        var replacement = new DisposableSpy();
        indexer.RegisterDriveMonitor("C", replacement);
        Assert.IsFalse(replacement.WasDisposed);
    }

    [TestMethod]
    public void RemoveDriveMonitor_NoEntryRegistered_DoesNotThrow() =>
        new UsnIndexer().RemoveDriveMonitor("C");

    [TestMethod]
    public void RemoveDriveMonitor_OnlyRemovesTheNamedDrive_LeavesOthersRunning()
    {
        var indexer = new UsnIndexer();
        var driveC = new DisposableSpy();
        var driveD = new DisposableSpy();
        indexer.RegisterDriveMonitor("C", driveC);
        indexer.RegisterDriveMonitor("D", driveD);

        indexer.RemoveDriveMonitor("C");

        Assert.IsTrue(driveC.WasDisposed);
        Assert.IsFalse(driveD.WasDisposed);
    }

    // Regression coverage: IsComplete must survive the FileRecordStore -> DriveRuntimeMetadata trip, since
    // UsnIndexerCacheExtensions.IsDriveIndexComplete (the local-drive counterpart of NetworkIndexer.Configure's
    // own IsComplete-gated cold-start resume) reads it off THIS metadata, not the store directly.
    [TestMethod]
    public void CreateMetadata_StoreIsComplete_PropagatesToMetadata()
    {
        var store = new FileRecordStore { IsComplete = true };

        var metadata = UsnIndexer.CreateMetadata(store);

        Assert.IsTrue(metadata.IsComplete);
    }

    [TestMethod]
    public void CreateMetadata_StoreNotComplete_PropagatesToMetadata()
    {
        var store = new FileRecordStore { IsComplete = false };

        var metadata = UsnIndexer.CreateMetadata(store);

        Assert.IsFalse(metadata.IsComplete);
    }

    private sealed class DisposableSpy : IDisposable
    {
        public bool WasDisposed { get; private set; }
        public void Dispose() => WasDisposed = true;
    }
}
