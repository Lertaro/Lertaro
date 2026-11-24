using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.DriveMonitoring;

// A re-index request raised while the same drive's own rebuild is still running -- the missed-folder-change
// follow-up at the end of that rebuild, or a monitor's request -- used to be refused as a duplicate and lost.
// Each rebuild here runs against an unmounted letter, so it fails fast and parks the drive without touching a
// volume; that is enough, because the rerun is decided after the rebuild whatever its outcome, short of a Stop.
// The lifetime source is cancelled but never disposed: the parked drive's re-insertion waiter is still
// registered on it (SearchEngine cancels without disposing for the same reason).
[TestClass]
public sealed class SearchEngineDriveMaintenanceTests
{
    private static string FreeDriveLetter() =>
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).First(l => !VolumeHelper.DetectIndexableLocalDrives().Contains(l));

    private static SearchEngineDriveMaintenance CreateMaintenance(UsnIndexer indexer, CancellationTokenSource lifetime) =>
        new(indexer, () => new MachineSettings(), () => lifetime.Token, () => false, () => { });

    [TestMethod]
    public void QueueDriveRebuild_RequestedWhileThatDriveRebuilds_RunsOnceAfterIt()
    {
        var lifetime = new CancellationTokenSource();
        using var indexer = new UsnIndexer();
        var maintenance = CreateMaintenance(indexer, lifetime);
        var queued = new List<Action>();
        var drive = FreeDriveLetter();

        Assert.IsTrue(maintenance.QueueDriveRebuild(drive, forceRebuild: true, queued.Add));
        // Two requests during one rebuild collapse into one rerun.
        maintenance.QueueDriveRebuild(drive);
        maintenance.QueueDriveRebuild(drive);
        queued[0]();

        Assert.HasCount(2, queued);
        Assert.IsTrue(maintenance.HasPendingRebuilds);
        lifetime.Cancel();
        queued[1]();
        Assert.HasCount(2, queued);
        Assert.IsFalse(maintenance.HasPendingRebuilds);
    }

    // The loop the dropped request used to prevent by accident: a busy FAT32/exFAT drive's old folder watcher
    // flags a missed change on every walk, so every rebuild asks for another. The cap only stops a regression
    // from hanging the test.
    [TestMethod]
    public void QueueDriveRebuild_EveryRebuildAsksForAnother_RerunsOnlyOnce()
    {
        var lifetime = new CancellationTokenSource();
        using var indexer = new UsnIndexer();
        var maintenance = CreateMaintenance(indexer, lifetime);
        var queued = new Queue<Action>();
        var drive = FreeDriveLetter();
        var runs = 0;

        Assert.IsTrue(maintenance.QueueDriveRebuild(drive, forceRebuild: false, queued.Enqueue));
        while (queued.Count > 0 && runs < 10)
        {
            maintenance.QueueDriveRebuild(drive);
            queued.Dequeue()();
            runs++;
        }
        lifetime.Cancel();

        Assert.AreEqual(2, runs);
        Assert.IsFalse(maintenance.HasPendingRebuilds);
    }

    [TestMethod]
    public void QueueDriveRebuild_RequestedBeforeAStop_DoesNotRerun()
    {
        var lifetime = new CancellationTokenSource();
        using var indexer = new UsnIndexer();
        var maintenance = CreateMaintenance(indexer, lifetime);
        var queued = new List<Action>();
        var drive = FreeDriveLetter();

        Assert.IsTrue(maintenance.QueueDriveRebuild(drive, forceRebuild: true, queued.Add));
        maintenance.QueueDriveRebuild(drive);
        Assert.IsTrue(maintenance.CancelDriveRebuild(drive));
        queued[0]();
        lifetime.Cancel();

        Assert.ContainsSingle(queued);
        Assert.IsFalse(maintenance.HasPendingRebuilds);
    }
}
