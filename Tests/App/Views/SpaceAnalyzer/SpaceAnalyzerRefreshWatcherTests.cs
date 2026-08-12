using Lertaro.App.Views.SpaceAnalyzer;

namespace Lertaro.App.Tests.Views.SpaceAnalyzer;

[TestClass]
public sealed class SpaceAnalyzerRefreshWatcherTests
{
    [TestMethod]
    public void ResolveWatchedPaths_RootLocationWatchesEveryDriveLetter()
    {
        var watched = SpaceAnalyzerRefreshWatcher.ResolveWatchedPaths([null]);

        Assert.HasCount(26, watched);
        Assert.Contains(@"C:\", watched);
        Assert.Contains(@"Z:\", watched);
    }

    [TestMethod]
    public void ResolveWatchedPaths_NestedLocationIncludesEveryBreadcrumbAncestor()
    {
        var watched = SpaceAnalyzerRefreshWatcher.ResolveWatchedPaths(
            [null, @"C:\", @"C:\Projects", @"c:\projects\App"]);

        CollectionAssert.AreEqual(new[] { @"C:\", @"C:\Projects", @"c:\projects\App" }, watched);
    }
}
