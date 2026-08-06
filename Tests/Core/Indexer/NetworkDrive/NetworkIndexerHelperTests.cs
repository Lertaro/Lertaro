using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

// NetworkIndex itself isn't exercised here -- constructing a real one requires writing a live snapshot
// file via NetworkDriveCacheLocator's real cache path, the same non-injectable-real-path hazard as
// UserSettings (see UserSettingsPluginSettingTests). CreateStatus's index/current fallback chain is fully
// covered via its null-index paths instead.
[TestClass]
public sealed class NetworkIndexerHelperTests
{
    [TestMethod]
    public void CreateStatus_NoIndexNoCurrent_UsesZeroDefaults()
    {
        var status = NetworkIndexerHelper.CreateStatus("Z", "Idle", 0, index: null, current: null);

        Assert.AreEqual("Z", status.Drive);
        Assert.AreEqual("Idle", status.State);
        Assert.AreEqual(0, status.Items);
        Assert.AreEqual(0, status.Skipped);
        Assert.AreEqual(0, status.Errors);
        Assert.IsNull(status.LastUpdated);
        Assert.AreEqual(string.Empty, status.Error);
    }

    [TestMethod]
    public void CreateStatus_NoIndexWithCurrent_FallsBackToCurrentValues()
    {
        var current = new NetworkIndexStatus
        {
            Skipped = 5,
            Errors = 6,
            EnumerateErrors = 7,
            AttributeErrors = 8,
            ReparseSkipped = 9,
            SlowDirectories = 10,
            CachePath = @"C:\cache\z",
            LastUpdated = new DateTime(2024, 1, 1)
        };

        var status = NetworkIndexerHelper.CreateStatus("Z", "Refreshing", 42, index: null, current);

        Assert.AreEqual(42, status.Items);
        Assert.AreEqual(5, status.Skipped);
        Assert.AreEqual(6, status.Errors);
        Assert.AreEqual(7, status.EnumerateErrors);
        Assert.AreEqual(8, status.AttributeErrors);
        Assert.AreEqual(9, status.ReparseSkipped);
        Assert.AreEqual(10, status.SlowDirectories);
        Assert.AreEqual(@"C:\cache\z", status.CachePath);
        Assert.AreEqual(current.LastUpdated, status.LastUpdated);
    }

    [TestMethod]
    public void CreateStatus_ErrorDefaultsToEmptyString_WhenNotSpecified()
    {
        var status = NetworkIndexerHelper.CreateStatus("Z", "Idle", 0, index: null, current: null);

        Assert.AreEqual(string.Empty, status.Error);
    }

    [TestMethod]
    public void CreateStatus_ErrorMessage_IsPassedThrough()
    {
        var status = NetworkIndexerHelper.CreateStatus("Z", "Error", 0, index: null, current: null, error: "disk offline");

        Assert.AreEqual("disk offline", status.Error);
    }

    [TestMethod]
    public void CreateStatus_NoCurrent_CachePathFallsBackToComputedPath()
    {
        var status = NetworkIndexerHelper.CreateStatus("Z", "Idle", 0, index: null, current: null);

        Assert.AreEqual(IndexerHelper.GetCachePath("Z"), status.CachePath);
    }
}
