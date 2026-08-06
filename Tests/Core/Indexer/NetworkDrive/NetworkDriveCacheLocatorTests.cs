using Lertaro.Core.Indexer.NetworkDrive;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive;

[TestClass]
public sealed class NetworkDriveCacheLocatorTests
{
    [TestMethod]
    public void GetIdForUnc_IsDeterministic()
    {
        var a = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share");
        var b = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void GetIdForUnc_CaseInsensitive()
    {
        var lower = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share");
        var upper = NetworkDriveCacheLocator.GetIdForUnc(@"\\SERVER\SHARE");

        Assert.AreEqual(lower, upper);
    }

    [TestMethod]
    public void GetIdForUnc_ForwardSlashAndTrailingSeparator_NormalizedTheSame()
    {
        var a = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share");
        var b = NetworkDriveCacheLocator.GetIdForUnc(@"//server/share/");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void GetIdForUnc_DifferentShares_ProduceDifferentIds()
    {
        var a = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share1");
        var b = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share2");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void GetIdForUnc_IsLowercaseHex()
    {
        var id = NetworkDriveCacheLocator.GetIdForUnc(@"\\server\share");

        Assert.AreEqual(64, id.Length); // SHA256 = 32 bytes = 64 hex chars
        Assert.AreEqual(id.ToLowerInvariant(), id);
    }
}
