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

    [TestMethod]
    public void ResolveRefreshKind_CurrentDirectoryChanged_ReloadsContent()
    {
        var kind = SpaceAnalyzerRefreshWatcher.ResolveRefreshKind(false,
            [@"C:\", @"C:\Projects", @"C:\Projects\App"],
            [@"C:\", @"C:\Projects", @"C:\Projects\App"]);

        Assert.AreEqual(SpaceAnalyzerRefreshKind.Reload, kind);
    }

    [TestMethod]
    public void ResolveRefreshKind_OnlyAncestorChanged_ValidatesLocationWithoutReloadingContent()
    {
        var kind = SpaceAnalyzerRefreshWatcher.ResolveRefreshKind(false,
            [@"C:\", @"C:\Projects", @"C:\Projects\App"],
            [@"C:\", @"C:\Projects"]);

        Assert.AreEqual(SpaceAnalyzerRefreshKind.ValidateLocation, kind);
    }

    [TestMethod]
    public void ResolveRefreshKind_RootViewChanged_ReloadsContent()
    {
        var kind = SpaceAnalyzerRefreshWatcher.ResolveRefreshKind(true, [@"C:\", @"D:\"], [@"C:\"]);

        Assert.AreEqual(SpaceAnalyzerRefreshKind.Reload, kind);
    }
}
