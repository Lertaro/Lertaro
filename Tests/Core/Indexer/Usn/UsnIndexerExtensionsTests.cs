using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

// ApplyFolderChange's own persist path (SaveDriveSnapshot -> LiveIndex.Compact) writes a real file under
// Logger.UserDataDir -- the same non-injectable-real-path hazard NetworkIndexerPublisherTests' own header
// comment calls out -- so, mirroring that suite, only the in-memory routing/bookkeeping (does the change
// land in the delta, does it flag or skip a persist) is covered here, not the actual disk write.
[TestClass]
public sealed class UsnIndexerExtensionsTests
{
    // Regression coverage for the local-drive counterpart of the network-drive rescan race: a
    // non-journaled drive's FolderDriveMonitor now stays alive for the whole rebuild (see
    // ApplyFolderChange's own comment on why), so a change landing mid-rebuild must be recorded as
    // missed instead of persisted against the doomed old LiveIndex -- ConsumeMissedFolderChangeDuringRebuild
    // is how the rebuild's own caller finds out it needs to queue a follow-up refresh.
    [TestMethod]
    public void ApplyFolderChange_DriveCurrentlyIndexing_AppliesChangeButFlagsItAsMissedInsteadOfPersisting()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });

        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        var (files, _) = fixture.Index.GetCounts();
        Assert.AreEqual(1, files);
        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    [TestMethod]
    public void ApplyFolderChange_DriveNotIndexing_DoesNotFlagAMissedChange()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });

        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    // Regression coverage for the race this whole change opened up: keeping the drive's monitor alive
    // throughout the rebuild (instead of stopping it beforehand) means a watcher callback can now be
    // genuinely in flight -- past the _recordIndexes lookup, about to call live.Mutate -- at the exact
    // moment OnDriveCompleted's DropDriveFromRuntime disposes that same old LiveIndex. Disposing the
    // fixture's index directly (while it's still the one sitting in _recordIndexes) reproduces that
    // exact ordering deterministically, without needing real threads.
    [TestMethod]
    public void ApplyFolderChange_LiveIndexDisposedConcurrentlyByARebuildCompleting_FlagsMissedInsteadOfThrowing()
    {
        // Not `using` -- fixture.Index is deliberately disposed early below to reproduce the race, and
        // LiveIndex.Dispose() isn't itself safe to call twice (its own _lock is a ReaderWriterLockSlim,
        // which throws ObjectDisposedException on a second Dispose -- disposing fixture again in the
        // normal way would fail cleanup, not the test itself, so it's swallowed in the finally below).
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        try
        {
            var indexer = new UsnIndexer();
            indexer._recordIndexes["C"] = fixture.Index;
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });
            fixture.Index.Dispose();

            indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, @"C:\somewhere\new-file.txt");

            Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        }
        finally
        {
            try { fixture.Dispose(); } catch { }
        }
    }

    // Regression coverage for the debounced-persist counterpart of the same race: SaveDriveSnapshot runs
    // on a background Timer callback up to a second after ApplyFolderChange scheduled it (KeyedDebouncer),
    // and DropDriveFromRuntime's own Cancel only stops a timer that HASN'T fired yet -- one already
    // mid-flight when a rebuild's Dispose() runs isn't stopped by it, so a rebuild finishing for this
    // drive while an old debounced save is still executing hits the exact same disposed-LiveIndex race.
    [TestMethod]
    public void SaveDriveSnapshot_LiveIndexDisposedConcurrently_DoesNotThrow()
    {
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        try
        {
            var indexer = new UsnIndexer();
            indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata { JournalId = 1, NextUsn = 100 };
            fixture.Index.Dispose();

            indexer.SaveDriveSnapshot("C", fixture.Index);
        }
        finally
        {
            try { fixture.Dispose(); } catch { }
        }
    }

    [TestMethod]
    public void ApplyFolderChange_UnknownDrive_DoesNotThrowOrFlagAMissedChange()
    {
        var indexer = new UsnIndexer();

        indexer.ApplyFolderChange("Z", WatcherChangeTypes.Changed, @"Z:\file.txt");

        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("Z"));
    }

    [TestMethod]
    public void ConsumeMissedFolderChangeDuringRebuild_NothingMissed_ReturnsFalse() =>
        Assert.IsFalse(new UsnIndexer().ConsumeMissedFolderChangeDuringRebuild("C"));

    [TestMethod]
    public void ConsumeMissedFolderChangeDuringRebuild_CalledTwice_OnlyTheFirstReturnsTrue()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "new-file.txt");
        File.WriteAllText(filePath, "x");

        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Changed, filePath);

        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        // Must not carry over to whatever the drive's next rebuild checks -- a stale true here would
        // queue a redundant follow-up refresh forever.
        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
