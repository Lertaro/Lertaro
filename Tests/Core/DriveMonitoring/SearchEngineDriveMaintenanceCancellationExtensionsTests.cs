using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class SearchEngineDriveMaintenanceCancellationExtensionsTests
{
    private static SearchEngineDriveMaintenance CreateMaintenance() => new(
        new UsnIndexer(),
        () => new MachineSettings(),
        () => CancellationToken.None,
        () => false,
        () => { });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CancelDriveRebuild_WorkStillQueued_CancelsRegisteredTokenBeforeExecution(bool forceRebuild)
    {
        using var lifetime = new CancellationTokenSource();
        using var indexer = new UsnIndexer();
        var completed = 0;
        var queued = new List<Action>();
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => lifetime.Token, () => false, () => completed++);
        const string drive = "QODER-TEST-REBUILD";

        Assert.IsTrue(maintenance.QueueDriveRebuild(drive, forceRebuild, queued.Add));
        Assert.IsTrue(maintenance._activeRebuildCts.TryGetValue(drive, out var cts));
        Assert.IsTrue(maintenance.CancelDriveRebuild(drive));
        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.IsFalse(maintenance.QueueDriveRebuild(drive, forceRebuild, queued.Add));
        lifetime.Cancel();

        Assert.ContainsSingle(queued)();

        Assert.AreEqual(1, completed);
        Assert.IsFalse(maintenance.HasPendingRebuilds);
        Assert.IsEmpty(maintenance._activeRebuildCts);
    }

    [TestMethod]
    public void CancelDriveRebuild_DriveHasAnActiveRebuild_CancelsItsTokenAndReturnsTrue()
    {
        var maintenance = CreateMaintenance();
        using var cts = new CancellationTokenSource();
        maintenance._activeRebuildCts["C"] = cts;

        var result = maintenance.CancelDriveRebuild("C");

        Assert.IsTrue(result);
        Assert.IsTrue(cts.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelDriveRebuild_NoActiveRebuildForThisDrive_ReturnsFalse()
    {
        var maintenance = CreateMaintenance();

        var result = maintenance.CancelDriveRebuild("C");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void CancelDriveRebuild_OnlyCancelsTheNamedDrive_LeavesOthersRunning()
    {
        var maintenance = CreateMaintenance();
        using var ctsC = new CancellationTokenSource();
        using var ctsD = new CancellationTokenSource();
        maintenance._activeRebuildCts["C"] = ctsC;
        maintenance._activeRebuildCts["D"] = ctsD;

        maintenance.CancelDriveRebuild("C");

        Assert.IsTrue(ctsC.IsCancellationRequested);
        Assert.IsFalse(ctsD.IsCancellationRequested);
    }

    [TestMethod]
    public void CancelDriveRebuild_NormalizesDriveLetterCasingAndSuffix()
    {
        var maintenance = CreateMaintenance();
        using var cts = new CancellationTokenSource();
        maintenance._activeRebuildCts["C"] = cts;

        var result = maintenance.CancelDriveRebuild("c:\\");

        Assert.IsTrue(result);
        Assert.IsTrue(cts.IsCancellationRequested);
    }
}
