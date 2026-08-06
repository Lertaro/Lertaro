using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.DriveMonitoring;

// CancelDriveRebuild's own bookkeeping (does it find and cancel the right CTS) is pure dictionary logic,
// independent of the real filesystem/service-process work SearchEngineDriveMaintenance's other methods do
// -- so these tests seed _activeRebuildCts directly (internal, reachable from this assembly) rather than
// driving a real rebuild through RebuildDriveIndex.
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
