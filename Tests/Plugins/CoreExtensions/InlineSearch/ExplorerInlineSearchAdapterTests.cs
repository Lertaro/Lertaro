using Lertaro.Plugins.CoreExtensions.InlineSearch;

namespace Lertaro.Plugins.CoreExtensions.Tests.InlineSearch;

[TestClass]
public sealed class ExplorerInlineSearchAdapterTests
{
    [TestMethod]
    public void ShouldDeferDirectOpenToApp_DefersDesktopItems()
    {
        Assert.IsTrue(ExplorerInlineSearchAdapter.ShouldDeferDirectOpenToApp(true, false, true));
        Assert.IsTrue(ExplorerInlineSearchAdapter.ShouldDeferDirectOpenToApp(true, true, false));
    }

    [TestMethod]
    public void ShouldDeferDirectOpenToApp_DefersFilesWhenAlwaysOpenIsEnabled()
    {
        Assert.IsTrue(ExplorerInlineSearchAdapter.ShouldDeferDirectOpenToApp(false, false, true));
    }

    [TestMethod]
    public void ShouldDeferDirectOpenToApp_KeepsExplorerNavigationInTheHook()
    {
        Assert.IsFalse(ExplorerInlineSearchAdapter.ShouldDeferDirectOpenToApp(false, true, true));
        Assert.IsFalse(ExplorerInlineSearchAdapter.ShouldDeferDirectOpenToApp(false, false, false));
    }
}
