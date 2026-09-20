using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// The diff that identifies the tab a request just created. The rest of the flow -- asking UI Automation
// for the tab, then driving it over COM -- needs a live Explorer window.
[TestClass]
public sealed class ExplorerTabLocatorTests
{
    [TestMethod]
    public void FirstNewTabHandle_NewHandleAppeared_ReturnsIt()
    {
        var tabsBefore = new HashSet<IntPtr> { (IntPtr)1, (IntPtr)2 };

        Assert.AreEqual((IntPtr)3, ExplorerTabLocator.FirstNewTabHandle(tabsBefore, [(IntPtr)2, (IntPtr)3, (IntPtr)1]));
    }

    [TestMethod]
    public void FirstNewTabHandle_NothingNew_ReturnsZero()
    {
        var tabsBefore = new HashSet<IntPtr> { (IntPtr)1, (IntPtr)2 };

        Assert.AreEqual(IntPtr.Zero, ExplorerTabLocator.FirstNewTabHandle(tabsBefore, [(IntPtr)2, (IntPtr)1]));
    }

    [TestMethod]
    public void FirstNewTabHandle_NoTabsAtAll_ReturnsZero() =>
        Assert.AreEqual(IntPtr.Zero, ExplorerTabLocator.FirstNewTabHandle(new HashSet<IntPtr>(), []));

    [TestMethod]
    public void FirstNewTabHandle_WindowClosedMidRequest_ReturnsZero() =>
        // Every tab gone (the window was closed while the request was in flight) has to read as "no new
        // tab", so the caller falls back instead of driving a handle that no longer exists.
        Assert.AreEqual(IntPtr.Zero, ExplorerTabLocator.FirstNewTabHandle(new HashSet<IntPtr> { (IntPtr)7 }, []));
}
