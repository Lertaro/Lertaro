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

        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path));
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

        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path));

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

        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path));
        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path));
    }

    [TestMethod]
    public void CompactIdleDeltas_NonJournalFileSystem_IsSkipped()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture, "exFAT");
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path));
        Assert.IsFalse(File.Exists(CachePath(tempDir.Path)));
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

    private static void AddChurn(LiveIndex index, int count) => index.Mutate((_, delta) =>
    {
        for (var i = 0; i < count; i++)
            DeltaLinkOps.AddLink(delta, (ulong)(i + 2), 1, $"churn{i}.txt", FileRecordFlags.None);
    });

    private static string CachePath(string cacheDir) => LocalDriveCacheLocator.GetCachePath(cacheDir, "C");

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
