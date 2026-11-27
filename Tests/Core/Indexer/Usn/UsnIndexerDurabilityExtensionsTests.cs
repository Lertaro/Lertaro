using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

// The two things this guards, both of which are invisible from the outside when wrong: a watermark that
// advances past changes nothing applied (permanently un-replayable), and a delta that never reaches the
// cache file at all (so every restart replays the whole session, bounded by the volume's journal window).
[TestClass]
public sealed class UsnIndexerDurabilityExtensionsTests
{
    private const ulong JournalId = 7;
    private const long StartUsn = 100;

    [TestMethod]
    public void AdvanceJournalWatermark_SameJournal_RecordsThePositionToStampWith()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = Metadata("NTFS");

        indexer.AdvanceJournalWatermark("C", JournalId, 500);

        Assert.AreEqual(500L, indexer._driveMetadata["C"].NextUsn);
        Assert.IsFalse(indexer.IsJournalWatermarkPinned("C"));
    }

    [TestMethod]
    public void AdvanceJournalWatermark_JournalRecreatedMidSession_PinsInsteadOfMoving()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = Metadata("NTFS");

        indexer.AdvanceJournalWatermark("C", JournalId + 1, 500);

        Assert.AreEqual(StartUsn, indexer._driveMetadata["C"].NextUsn);
        Assert.IsTrue(indexer.IsJournalWatermarkPinned("C"));
    }

    // The pin is what keeps a dropped batch replayable, so it has to outlast every later batch, which all
    // arrive with the SAME journal id the metadata already holds.
    [TestMethod]
    public void AdvanceJournalWatermark_AfterPinning_IgnoresLaterBatches()
    {
        var indexer = new UsnIndexer();
        indexer._driveMetadata["C"] = Metadata("NTFS");
        indexer.PinJournalWatermark("C");

        indexer.AdvanceJournalWatermark("C", JournalId, 500);

        Assert.AreEqual(StartUsn, indexer._driveMetadata["C"].NextUsn);
    }

    // A rebuild or cold-start catch-up installs a fresh metadata over a consistent index/watermark pair,
    // which clears the pin by itself -- no per-drive bookkeeping to forget to reset.
    [TestMethod]
    public void PinJournalWatermark_IsNotInheritedByTheNextMetadataInstance()
    {
        var indexer = new UsnIndexer();
        indexer.PinJournalWatermark("C");
        indexer._driveMetadata["C"] = Metadata("NTFS");

        indexer.AdvanceJournalWatermark("C", JournalId, 500);

        Assert.AreEqual(500L, indexer._driveMetadata["C"].NextUsn);
    }

    [TestMethod]
    public void AdvanceJournalWatermark_DriveNoLongerLoaded_IsIgnored()
    {
        var indexer = new UsnIndexer();

        indexer.AdvanceJournalWatermark("C", JournalId, 500);

        Assert.IsFalse(indexer.IsJournalWatermarkPinned("C"));
    }

    [TestMethod]
    public void CompactIdleDeltas_BelowThreshold_WritesNoCacheFile()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "NTFS");

        fixture.Index.Mutate((_, delta) => DeltaLinkOps.AddLink(delta, 2, 1, "one.txt", FileRecordFlags.None));

        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path, null));
        Assert.IsFalse(File.Exists(CachePath(tempDir.Path)));
    }

    [TestMethod]
    public void CompactIdleDeltas_AboveThreshold_FoldsTheDeltaAndStampsTheLiveWatermark()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "NTFS");
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);
        indexer.AdvanceJournalWatermark("C", JournalId, 4242);

        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path, null));

        var header = SnapshotFormat.TryReadHeaderFromFile(CachePath(tempDir.Path));
        Assert.IsNotNull(header);
        Assert.AreEqual(4242L, header.NextUsn);
        Assert.AreEqual(JournalId, header.JournalId);
        // Root + the whole folded delta: the rows really made it into the file, not just the stamp.
        Assert.AreEqual(1 + UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold, header.RowCount);
    }

    // The threshold is self-closing rather than timed: Compact swaps in an empty DeltaOverlay, so the same
    // idle tick can't immediately pay for another full rewrite.
    [TestMethod]
    public void CompactIdleDeltas_RunAgainRightAfter_WritesNothing()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "NTFS");
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path, null));
        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path, null));
    }

    // A FAT32/exFAT drive no longer persists on every watcher change (#298); this threshold is now its
    // only in-session persist, so it must not be skipped.
    [TestMethod]
    public void CompactIdleDeltas_NonJournalFileSystem_AboveThreshold_IsWritten()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "exFAT");
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path, null));
        Assert.IsTrue(File.Exists(CachePath(tempDir.Path)));
    }

    // Mid-rebuild, the rebuild's own write owns the cache path and this LiveIndex is about to be replaced.
    [TestMethod]
    public void CompactIdleDeltas_NonJournalFileSystemWhileRebuilding_IsSkipped()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "exFAT");
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path, null));
        Assert.IsFalse(File.Exists(CachePath(tempDir.Path)));
    }

    // This runs on SearchEngine's idle Timer, where an escaping exception ends the process. A LiveIndex a
    // rebuild disposed must be skipped, not read.
    [TestMethod]
    [DataRow("NTFS")]
    [DataRow("exFAT")]
    public void CompactIdleDeltas_LiveIndexDisposed_SkipsItWithoutThrowing(string fileSystemType)
    {
        using var tempDir = new TempDirectory();
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, fileSystemType);
        fixture.Index.Dispose();

        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path, null));
        Assert.IsFalse(File.Exists(CachePath(tempDir.Path)));
        try { fixture.Dispose(); } catch { }
    }

    // A write that fails and cannot even reopen the snapshot on disk leaves the LiveIndex unusable, and the
    // FAT32/exFAT drive serving nothing: it owes a re-walk, which starts from no baseline. Staged by putting a
    // directory where the cache file was, so the write cannot replace it and the reopen cannot open it.
    [TestMethod]
    public void CompactIdleDeltas_FolderDriveLeftUnusable_AsksForARewalkFromNoBaseline()
    {
        using var tempDir = new TempDirectory();
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "exFAT");
        var requested = new List<string>();

        LeaveUnusable(tempDir.Path, fixture.Index, () => indexer.CompactIdleDeltas(tempDir.Path, requested.Add));

        CollectionAssert.AreEqual(new[] { "C" }, requested);
        Assert.IsNull(indexer.GetWalkBaseline("C"));
        indexer._recordIndexes.Clear();
        try { fixture.Dispose(); } catch { }
    }

    // Two idle passes can both have selected a drive: the second selects it while the first is still busy
    // compacting another drive. Then the pass whose reopen failed and the pass that finds the index it left
    // unusable both fail on it, and only the first owes the re-walk. Pass A is parked on D's Compact by a
    // reader holding D's index, after it selected [D, C]; pass B (D now rebuilding, so skipped) leaves C
    // unusable; then pass A reaches C.
    [TestMethod]
    public void CompactIdleDeltas_TwoPassesSelectedTheSameDrive_OneUnusableIndexAsksForOneRewalk()
    {
        using var tempDir = new TempDirectory();
        var c = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        using var d = LiveIndexFixture.Build("D", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        var requests = new List<string>();
        void Request(string drive) { lock (requests) requests.Add(drive); }
        using var release = new ManualResetEventSlim();
        using var holding = new ManualResetEventSlim();
        Thread? holder = null, passA = null;
        try
        {
            // Inserted D first so a pass compacts D before C.
            indexer._recordIndexes["D"] = d.Index;
            indexer._driveMetadata["D"] = Metadata("exFAT");
            indexer._recordIndexes["C"] = c.Index;
            indexer._driveMetadata["C"] = Metadata("exFAT");
            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "D", State = "indexing" });
            LeaveUnusableOnNextWrite(tempDir.Path, c.Index, () => indexer.CompactIdleDeltas(tempDir.Path, null));
            indexer.Status.Drives.Clear();
            AddChurn(d.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

            holder = new Thread(() => d.Index.Read((_, _) => { holding.Set(); release.Wait(); return 0; })) { IsBackground = true };
            holder.Start();
            Assert.IsTrue(holding.Wait(TimeSpan.FromSeconds(30)));
            passA = new Thread(() => indexer.CompactIdleDeltas(tempDir.Path, Request)) { IsBackground = true };
            passA.Start();
            Assert.IsTrue(SpinWait.SpinUntil(() => (passA.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(30)),
                "precondition: pass A is parked on D's Compact");

            indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "D", State = "indexing" });
            indexer.CompactIdleDeltas(tempDir.Path, Request);
            release.Set();
            Assert.IsTrue(passA.Join(TimeSpan.FromSeconds(30)));

            CollectionAssert.AreEqual(new[] { "C" }, requests);
        }
        finally
        {
            release.Set();
            holder?.Join(TimeSpan.FromSeconds(5));
            passA?.Join(TimeSpan.FromSeconds(5));
            indexer._recordIndexes.Clear();
            try { c.Dispose(); } catch { }
        }
    }

    // A journal drive's recovery from a lost delta is not a re-walk: it also has to keep the watermark from
    // stamping the lost changes as persisted, which is a separate fix. Nothing is asked for from here.
    [TestMethod]
    public void CompactIdleDeltas_JournalDriveLeftUnusable_IsNotReWalked()
    {
        using var tempDir = new TempDirectory();
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "NTFS");
        var requested = new List<string>();

        LeaveUnusable(tempDir.Path, fixture.Index, () => indexer.CompactIdleDeltas(tempDir.Path, requested.Add));

        Assert.IsEmpty(requested);
        indexer._recordIndexes.Clear();
        try { fixture.Dispose(); } catch { }
    }

    // A write that fails while the snapshot on disk reopens leaves the index serving. Re-walking would not be
    // safe: the walk's own hand-in drops the index before writing its file, so a write failure that persists
    // (here, a read-only cache file) would leave the drive with no index at all. A journal drive is never
    // re-walked from here either (its recovery is a separate fix).
    [TestMethod]
    [DataRow("exFAT")]
    [DataRow("NTFS")]
    public void CompactIdleDeltas_WriteFailsButTheSnapshotReopens_KeepsServingIt(string fileSystemType)
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, fileSystemType);
        var requested = new List<string>();
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);
        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path, requested.Add), "precondition: the first write lands");
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold, first: 100_000);
        File.SetAttributes(CachePath(tempDir.Path), FileAttributes.ReadOnly);
        try
        {
            Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path, requested.Add));
        }
        finally
        {
            File.SetAttributes(CachePath(tempDir.Path), FileAttributes.Normal);
        }

        Assert.IsEmpty(requested);
        Assert.AreEqual(0, fixture.Index.PendingChangeCount, "serving the reopened snapshot");
        Assert.IsNotNull(indexer.GetWalkBaseline("C"));
        indexer._recordIndexes.Clear();
    }

    // The idle Timer keeps running while a startup does, when the engine refuses every rebuild request (see
    // SearchEngineDriveMaintenance.TryBeginRebuild): for instance a second InitializeOrLoadIndex (a client's
    // rebuild) while the previous index still holds enough pending changes, or the moment after a drive's
    // hand-over and before the startup completes. Handed the startup's RequestRebuild, as SearchEngine does,
    // the re-walk waits for the startup to finish instead of being dropped.
    [TestMethod]
    public void CompactIdleDeltas_FolderDriveLeftUnusableDuringStartup_ReWalkIsFlushedAfterIt()
    {
        using var tempDir = new TempDirectory();
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "exFAT");
        var starting = true;
        var accepted = new List<string>();
        var initializer = new SearchEngineInitializer(indexer, tempDir.Path, d => { if (!starting) accepted.Add(d); });

        LeaveUnusable(tempDir.Path, fixture.Index, () => indexer.CompactIdleDeltas(tempDir.Path, initializer.RequestRebuild));
        indexer._recordIndexes.Clear();
        try { fixture.Dispose(); } catch { }
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            initializer.Run(forceRebuild: true, cts, isRebuilding => starting = isRebuilding, new MachineSettings());
        }

        CollectionAssert.AreEqual(new[] { "C" }, accepted);
    }

    private static void LeaveUnusable(string cacheDir, LiveIndex index, Action compact)
    {
        LeaveUnusableOnNextWrite(cacheDir, index, compact);
        compact();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = index.PendingChangeCount, "precondition: left unusable");
    }

    // Lands one write of drive C, then puts a directory where its cache file was, so the next write cannot
    // replace it and the reopen cannot open it.
    private static void LeaveUnusableOnNextWrite(string cacheDir, LiveIndex index, Action compact)
    {
        AddChurn(index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);
        compact();
        Assert.IsTrue(File.Exists(CachePath(cacheDir)), "precondition: the first write lands");
        File.Delete(CachePath(cacheDir)); // the LiveIndex's mapping allows the delete
        Directory.CreateDirectory(CachePath(cacheDir));
        AddChurn(index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold, first: 100_000);
    }

    private static UsnIndexer.DriveRuntimeMetadata Metadata(string fileSystemType) => new()
    {
        SourceKind = FileRecordSourceKind.LocalMft,
        IdKind = FileRecordIdKind.MftFrn,
        FileSystemType = fileSystemType,
        JournalId = JournalId,
        NextUsn = StartUsn,
    };

    private static UsnIndexer Loaded(LiveIndexFixture fixture, string fileSystemType)
    {
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer._driveMetadata["C"] = Metadata(fileSystemType);
        return indexer;
    }

    private static void AddChurn(LiveIndex index, int count, int first = 0) => index.Mutate((_, delta) =>
    {
        for (var i = first; i < first + count; i++)
            DeltaLinkOps.AddLink(delta, (ulong)(i + 2), 1, $"churn{i}.txt", FileRecordFlags.None);
    });

    private static string CachePath(string cacheDir) => LocalDriveCacheLocator.GetCachePath(cacheDir, "C");

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
