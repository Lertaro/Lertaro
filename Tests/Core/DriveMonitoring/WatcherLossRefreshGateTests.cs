using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

// A drive whose FileSystemWatcher dies (a buffer overflow on a busy share is the usual cause) has silently
// lost every change made while it was down, so it must be re-walked -- exactly once per outage, not once
// per failed retry. The watcher itself is real OS plumbing with no injectable seam (FileSystemWatcher is
// constructed inside DriveWatcherHost, and its Error event needs a real buffer overflow to fire), so that
// decision lives in this collaborator and is what gets tested; the two call sites
// (WatcherManager.ConfigureWatcher, FolderDriveMonitor.ConfigureWatcher) remain covered only by inspection.
[TestClass]
public sealed class WatcherLossRefreshGateTests
{
    [TestMethod]
    public void ShouldRequestRefresh_FirstLossIsTheOnlyOneThatRequests()
    {
        var gate = new WatcherLossRefreshGate();

        Assert.IsTrue(gate.ShouldRequestRefresh("Z"));
        Assert.IsFalse(gate.ShouldRequestRefresh("Z"));
        Assert.IsFalse(gate.ShouldRequestRefresh("Z"));
    }

    [TestMethod]
    public void ShouldRequestRefresh_AfterRecovered_RequestsAgain()
    {
        var gate = new WatcherLossRefreshGate();
        gate.ShouldRequestRefresh("Z");

        gate.Recovered("Z");

        Assert.IsTrue(gate.ShouldRequestRefresh("Z"));
    }

    // The first watcher of an operating session is configured through the same path a recovery is, so a
    // Recovered call on a drive that never reported anything must not pre-suppress its first real loss.
    [TestMethod]
    public void Recovered_BeforeAnyLoss_StillLetsTheFirstLossRequest()
    {
        var gate = new WatcherLossRefreshGate();

        gate.Recovered("Z");

        Assert.IsTrue(gate.ShouldRequestRefresh("Z"));
    }

    [TestMethod]
    public void ShouldRequestRefresh_TracksEachDriveIndependentlyAndIgnoresCase()
    {
        var gate = new WatcherLossRefreshGate();

        Assert.IsTrue(gate.ShouldRequestRefresh(@"\\server\share"));
        Assert.IsTrue(gate.ShouldRequestRefresh("Z"));
        Assert.IsFalse(gate.ShouldRequestRefresh(@"\\SERVER\SHARE"));
        Assert.IsTrue(gate.ShouldRequestRefresh("y"));
    }
}
