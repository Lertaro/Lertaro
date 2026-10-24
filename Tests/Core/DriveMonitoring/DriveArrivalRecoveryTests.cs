using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.DriveMonitoring;

// These settle methods are the whole answer to "what happens to a drive whose volume stopped answering",
// which used to be a terminal state and nothing else -- no monitor, no catch-up, and a frozen index that
// kept serving searches with rows for files that no longer exist. The USN/PnP plumbing behind them has no
// injectable seam (a real volume handle, a real device notification), so what gets pinned here is the
// decision and its observable effects: the state the drive row ends in, whether the frozen index is
// parked, and which callback the caller is handed.
[TestClass]
public sealed class DriveArrivalRecoveryTests
{
    // The exact predicate the code under test uses, so "not mounted" here means not mounted in that list.
    private static string FreeDriveLetter() =>
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).First(l => !VolumeHelper.DetectIndexableLocalDrives().Contains(l));

    private static string MountedDriveLetter() =>
        DriveInfo.GetDrives().First(d => d.IsReady && d.DriveType != DriveType.Network).Name[0].ToString();

    private static UsnIndexer IndexerWith(string drive, string state, LiveIndexFixture? live)
    {
        var indexer = new UsnIndexer();
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = drive, State = state, Enabled = true });
        if (live != null)
        {
            indexer._recordIndexes[drive] = live.Index;
            indexer.Status.ActiveDrives.Add(drive);
        }
        return indexer;
    }

    // A token already cancelled at the moment it is handed over: DriveReattachWaiter builds its poll with
    // Task.Run(action, token), so an already-cancelled token leaves the action unrun and nothing ever
    // registers on it -- which is what makes disposing the source here safe, and what makes "the arrival
    // callback did not fire yet" a real assertion rather than a race.
    private static CancellationToken GoneToken()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        cts.Cancel();
        cts.Dispose();
        return token;
    }

    [TestMethod]
    public void SettleFailedBuild_MountedDriveAfterAStop_RevertsToCached()
    {
        var drive = MountedDriveLetter();
        var indexer = IndexerWith(drive, "indexing", null);

        DriveArrivalRecovery.SettleFailedBuild(indexer, drive, wasCancelled: true, GoneToken(), null);

        Assert.AreEqual("cached", indexer.Status.Drives.Single(d => d.Drive == drive).State);
    }

    [TestMethod]
    public void SettleFailedBuild_MountedDriveAfterAFailure_ReportsFailed()
    {
        var drive = MountedDriveLetter();
        var indexer = IndexerWith(drive, "indexing", null);

        DriveArrivalRecovery.SettleFailedBuild(indexer, drive, wasCancelled: false, GoneToken(), null);

        Assert.AreEqual("failed", indexer.Status.Drives.Single(d => d.Drive == drive).State);
    }

    [TestMethod]
    public void SettleFailedBuild_MountedDrive_KeepsItsLoadedIndex()
    {
        var drive = MountedDriveLetter();
        var live = LiveIndexFixture.Build(drive, new[] { LiveIndexFixture.Root() });
        var indexer = IndexerWith(drive, "indexing", live);

        DriveArrivalRecovery.SettleFailedBuild(indexer, drive, wasCancelled: false, GoneToken(), null);

        try
        {
            Assert.IsTrue(indexer._recordIndexes.ContainsKey(drive));
            Assert.IsTrue(indexer.Status.ActiveDrives.Contains(drive));
        }
        finally
        {
            // Parked branches dispose the index themselves; this one must not have touched it, and
            // LiveIndex.Dispose cannot be called twice.
            try { live.Dispose(); } catch { }
        }
    }

    // The branch this whole class exists for: the volume is gone, so neither "cached" nor "failed" is the
    // truth -- the drive is waiting to come back, and its unreconcilable index must stop answering queries.
    [TestMethod]
    public void SettleFailedBuild_VolumeIsGone_ParksTheIndexInsteadOfReportingFailure()
    {
        var drive = FreeDriveLetter();
        var indexer = IndexerWith(drive, "indexing", null);
        var reindexRequests = new List<string>();

        DriveArrivalRecovery.SettleFailedBuild(indexer, drive, wasCancelled: false, GoneToken(), d => reindexRequests.Add(d));

        Assert.AreEqual("unavailable", indexer.Status.Drives.Single(d => d.Drive == drive).State);
        Assert.IsFalse(indexer._recordIndexes.ContainsKey(drive));
        Assert.IsEmpty(reindexRequests);
    }

    [TestMethod]
    public void SettleMonitorGiveUp_MountedButUnreadable_RequestsAReindexOfThatDrive()
    {
        var drive = MountedDriveLetter();
        var live = LiveIndexFixture.Build(drive, new[] { LiveIndexFixture.Root() });
        var indexer = IndexerWith(drive, "indexing", live);
        var reindexRequests = new List<string>();

        DriveArrivalRecovery.SettleMonitorGiveUp(indexer, drive, GoneToken(), d => reindexRequests.Add(d));

        try
        {
            CollectionAssert.AreEqual(new[] { drive }, reindexRequests);
            Assert.IsTrue(indexer._recordIndexes.ContainsKey(drive));
        }
        finally
        {
            try { live.Dispose(); } catch { }
        }
    }

    [TestMethod]
    public void SettleMonitorGiveUp_VolumeIsGone_WaitsForReinsertionInsteadOfRequestingAReindex()
    {
        var drive = FreeDriveLetter();
        var indexer = IndexerWith(drive, "indexing", null);
        var reindexRequests = new List<string>();

        DriveArrivalRecovery.SettleMonitorGiveUp(indexer, drive, GoneToken(), d => reindexRequests.Add(d));

        Assert.IsEmpty(reindexRequests);
        Assert.AreEqual("unavailable", indexer.Status.Drives.Single(d => d.Drive == drive).State);
    }

    // A rebuild asked for a letter this machine never had must not park a waiter that polls for it for the
    // life of the process -- only a letter with a cached index is a drive somebody actually indexed.
    [TestMethod]
    public void ArmRebuildForReinsertion_NoCachedIndexForThatDrive_IsRefusedWithoutTouchingAnything()
    {
        using var dir = new TempDirectory();
        var drive = FreeDriveLetter();
        var indexer = IndexerWith(drive, "unavailable", null);

        var accepted = DriveArrivalRecovery.ArmRebuildForReinsertion(
            indexer, dir.Path, drive, GoneToken(), _ => { });

        Assert.IsFalse(accepted);
        Assert.AreEqual("unavailable", indexer.Status.Drives.Single(d => d.Drive == drive).State);
    }

    [TestMethod]
    public void ArmRebuildForReinsertion_CachedIndexExistsForTheAbsentDrive_IsAcceptedAndWaits()
    {
        using var dir = new TempDirectory();
        var drive = FreeDriveLetter();
        var cachePath = Path.Combine(dir.Path, "somekey.idx");
        SnapshotWriter.Write(BuildLocalMftStore(drive), cachePath);
        var indexer = IndexerWith(drive, "cached", null);
        var rebuildRequests = new List<string>();

        var accepted = DriveArrivalRecovery.ArmRebuildForReinsertion(
            indexer, dir.Path, drive, GoneToken(), d => rebuildRequests.Add(d));

        Assert.IsTrue(accepted);
        // Accepted, not run: the rebuild is what the re-insertion waiter will ask for.
        Assert.IsEmpty(rebuildRequests);
        Assert.AreEqual("unavailable", indexer.Status.Drives.Single(d => d.Drive == drive).State);
    }

    private static FileRecordStore BuildLocalMftStore(string sourceKey)
    {
        var store = new FileRecordStore
        {
            SourceKey = sourceKey,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.SourceLocalId64,
            RootId = 1,
        };
        store.Records.Add(new FileRecord(1, 1, string.Empty, FileRecordFlags.Directory | FileRecordFlags.SourceRoot));
        return store;
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
