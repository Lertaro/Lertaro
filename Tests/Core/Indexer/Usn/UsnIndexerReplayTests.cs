using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Indexer.Usn;

// A catch-up replay is the one batch that can carry tens of thousands of records, and it used to pay the
// live path's per-record stat and per-batch directory fan-out for every one of them -- work that competes
// with the very drain it is part of, which is how a monitor ends up behind the journal's own window.
// These pin the two costs a replay may skip and the one it must still pay: a link the index has never
// seen needs its stat, or its Size/timestamps stay zero and it drops out of every age-windowed query.
[TestClass]
public sealed class UsnIndexerReplayTests
{
    [TestMethod]
    public void ApplyUsnRecords_Live_NotifiesChangedDirectories()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture);
        var notified = 0;
        indexer.DirectoriesChanged += (_, _) => notified++;

        indexer.ApplyUsnRecords("C", new[] { CreateRecord(2, 1, "one.txt") });

        Assert.AreEqual(1, notified);
    }

    [TestMethod]
    public void ApplyUsnRecords_Replay_LeavesTheNotificationToTheCatchUp()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture);
        var notified = 0;
        indexer.DirectoriesChanged += (_, _) => notified++;

        indexer.ApplyUsnRecords("C", new[] { CreateRecord(2, 1, "one.txt") }, UsnApplyOrigin.Replay);

        Assert.AreEqual(0, notified);
    }

    // The bulk of a post-rebuild replay: records re-describing rows the scan already recorded.
    [TestMethod]
    public void ApplyUsnRecords_Replay_ReDescriptionOfAnIndexedLinkAddsNoRow()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
            new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        });
        var indexer = Loaded(fixture);
        var before = fixture.Index.ToStore().Records.Count;

        var applied = indexer.ApplyUsnRecords("C", new[] { CreateRecord(3, 2, "readme.txt") }, UsnApplyOrigin.Replay);

        Assert.IsTrue(applied);
        Assert.AreEqual(before, fixture.Index.ToStore().Records.Count);
    }

    // The other half: a file created while nothing was reading has no row and no metadata, so skipping
    // its stat would leave it Size 0 / LastWrite 0 until the next rebuild.
    [TestMethod]
    public void ApplyUsnRecords_Replay_NewLinkIsStillIndexed()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture);

        indexer.ApplyUsnRecords("C", new[] { CreateRecord(9, 1, "created-while-unmonitored.txt") }, UsnApplyOrigin.Replay);

        var record = fixture.Index.ToStore().Records.Single(x => x.Id == 9);
        Assert.AreEqual("created-while-unmonitored.txt", record.Name);
    }

    [TestMethod]
    public void AddLink_ReportsWhetherTheLinkWasActuallyNew()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "one.txt", FileRecordFlags.None),
        });

        fixture.Index.Mutate((_, delta) =>
        {
            Assert.IsFalse(DeltaLinkOps.AddLink(delta, 2, 1, "one.txt", FileRecordFlags.None));
            Assert.IsTrue(DeltaLinkOps.AddLink(delta, 3, 1, "two.txt", FileRecordFlags.None));
        });
    }

    [TestMethod]
    public void ReplayFlag_IsPerDriveAndCaseInsensitive()
    {
        var indexer = new UsnIndexer();

        Assert.IsFalse(indexer.IsReplayInProgress("C"));
        indexer.BeginReplay("c");
        Assert.IsTrue(indexer.IsReplayInProgress("C"));
        Assert.IsFalse(indexer.IsReplayInProgress("D"));
        indexer.EndReplay("C");
        Assert.IsFalse(indexer.IsReplayInProgress("C"));
    }

    // Compact holds the LiveIndex write lock across a full merge and snapshot rewrite; taking it in the
    // middle of a catch-up drain stalls the drain itself. Deferring costs nothing: the same delta is
    // still there for the next tick.
    [TestMethod]
    public void CompactIdleDeltas_DuringReplay_DefersToTheNextTick()
    {
        using var tempDir = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = Loaded(fixture);
        AddChurn(fixture.Index, UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold);

        indexer.BeginReplay("C");
        Assert.AreEqual(0, indexer.CompactIdleDeltas(tempDir.Path));
        Assert.IsFalse(File.Exists(LocalDriveCacheLocator.GetCachePath(tempDir.Path, "C")));

        indexer.EndReplay("C");
        Assert.AreEqual(1, indexer.CompactIdleDeltas(tempDir.Path));
    }

    private static ParsedUsnRecord CreateRecord(ulong frn, ulong parentFrn, string name) => new()
    {
        FileReferenceNumber = frn,
        ParentFileReferenceNumber = parentFrn,
        FileName = name,
        Reason = Win32Api.USN_REASON_FILE_CREATE,
    };

    private static UsnIndexer Loaded(LiveIndexFixture fixture)
    {
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer._driveMetadata["C"] = new UsnIndexer.DriveRuntimeMetadata
        {
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            FileSystemType = "NTFS",
        };
        return indexer;
    }

    private static void AddChurn(LiveIndex index, int count) => index.Mutate((_, delta) =>
    {
        for (var i = 0; i < count; i++)
            DeltaLinkOps.AddLink(delta, (ulong)(i + 2), 1, $"churn{i}.txt", FileRecordFlags.None);
    });

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
