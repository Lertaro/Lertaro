using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// The two decisions this class can state without a live Explorer: the diff that identifies the tab a request
// just created, and what one look at the shell means for whether it can host tabs at all. Asking UI
// Automation for the tab, and driving it over COM, both need a real Explorer window.
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

    [TestMethod]
    public void TabStripStateAfter_TabBearingWindow_Present() =>
        Assert.AreEqual(ExplorerTabLocator.TabStripPresent,
            ExplorerTabLocator.TabStripStateAfter(ExplorerTabLocator.TabStripUnknown, tabBearingWindow: true, anyExplorerWindow: true));

    [TestMethod]
    public void TabStripStateAfter_WindowWithoutAnyTab_Absent() =>
        // Windows 10 answers exactly like this, forever: a folder window exists and no ShellTabWindowClass
        // ever does. Learning it once is what stops every folder open from waiting two seconds on it.
        Assert.AreEqual(ExplorerTabLocator.TabStripAbsent,
            ExplorerTabLocator.TabStripStateAfter(ExplorerTabLocator.TabStripUnknown, tabBearingWindow: false, anyExplorerWindow: true));

    [TestMethod]
    public void TabStripStateAfter_NoWindowAtAllKeepsWhatWasKnown() =>
        // Nothing there yet says nothing about the shell: a cold Explorer behind a slow share can take longer
        // to appear than the wait gives it, and latching "no tabs" on that would switch the feature off on a
        // Windows 11 shell that has tabs.
        Assert.AreEqual(ExplorerTabLocator.TabStripUnknown,
            ExplorerTabLocator.TabStripStateAfter(ExplorerTabLocator.TabStripUnknown, tabBearingWindow: false, anyExplorerWindow: false));

    [TestMethod]
    public void TabStripStateAfter_LastLookWinsOverTheCachedOne() =>
        // A shell that showed tabs and now answers with a window that has none closed its last tab window;
        // the look in front of us is the current truth, and the probe runs again on the next request.
        Assert.AreEqual(ExplorerTabLocator.TabStripAbsent,
            ExplorerTabLocator.TabStripStateAfter(ExplorerTabLocator.TabStripPresent, tabBearingWindow: false, anyExplorerWindow: true));
}
