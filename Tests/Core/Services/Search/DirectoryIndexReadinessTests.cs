using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class DirectoryIndexReadinessTests
{
    [TestMethod]
    public void ReadyDriveIsReady()
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = "ready",
            Drives = new List<UsnIndexer.DriveIndexStatus>
            {
                new() { Drive = "D", State = "ready" }
            }
        };

        Assert.IsTrue(DirectoryIndexReadiness.IsLocalReady(status, "D"));
        Assert.IsFalse(DirectoryIndexReadiness.ShouldWaitForLocal(status, "D"));
    }

    [TestMethod]
    public void LoadingDriveMustBeWaitedFor()
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = "indexing",
            Drives = new List<UsnIndexer.DriveIndexStatus>
            {
                new() { Drive = "D", State = "indexing" }
            }
        };

        Assert.IsFalse(DirectoryIndexReadiness.IsLocalReady(status, "D"));
        Assert.IsTrue(DirectoryIndexReadiness.ShouldWaitForLocal(status, "D"));
    }

    [TestMethod]
    public void DisabledDriveIsNotWaitedFor()
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = "ready",
            Drives = new List<UsnIndexer.DriveIndexStatus>
            {
                new() { Drive = "D", State = "disabled" }
            }
        };

        Assert.IsFalse(DirectoryIndexReadiness.IsLocalReady(status, "D"));
        Assert.IsFalse(DirectoryIndexReadiness.ShouldWaitForLocal(status, "D"));
    }

    [TestMethod]
    public void ServiceErrorIsRetriedDuringReadinessWindow()
    {
        var status = new UsnIndexer.IndexerStatus { State = "error" };

        Assert.IsFalse(DirectoryIndexReadiness.IsLocalReady(status, "D"));
        Assert.IsFalse(DirectoryIndexReadiness.ShouldWaitForLocal(status, "D"));
    }

    [TestMethod]
    public void CachedInProcessIndexIsReady()
    {
        Assert.IsTrue(DirectoryIndexReadiness.IsInProcessReady(new NetworkIndexStatus { State = "cached" }));
        Assert.IsTrue(DirectoryIndexReadiness.IsInProcessReady(new NetworkIndexStatus { State = "ready" }));
        Assert.IsFalse(DirectoryIndexReadiness.IsInProcessReady(new NetworkIndexStatus { State = "indexing" }));
    }
}
