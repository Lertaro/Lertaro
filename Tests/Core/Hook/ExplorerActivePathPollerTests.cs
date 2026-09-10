using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class ExplorerActivePathPollerTests
{
    [TestMethod]
    public void IsObservedWindowStillActive_RequiresTheSameNonZeroHandle()
    {
        Assert.IsTrue(ExplorerActivePathPoller.IsObservedWindowStillActive(new IntPtr(1), new IntPtr(1)));
        Assert.IsFalse(ExplorerActivePathPoller.IsObservedWindowStillActive(new IntPtr(1), new IntPtr(2)));
        Assert.IsFalse(ExplorerActivePathPoller.IsObservedWindowStillActive(IntPtr.Zero, IntPtr.Zero));
    }

    [TestMethod]
    public void UpdatePath_UsesConfiguredPathNormalizer()
    {
        using var tracker = new ExplorerTracker { PathNormalizer = _ => string.Empty };

        tracker.UpdatePath(@"D:\Projects", isDesktop: false);

        Assert.AreEqual(string.Empty, tracker.ActivePath);
    }

    [TestMethod]
    public void UpdatePath_DialogSource_DoesNotReplaceLastExplorerPath()
    {
        using var tracker = new ExplorerTracker();

        tracker.UpdatePath(@"C:\Workspace", isDesktop: false, isDialog: false);
        tracker.UpdatePath(@"D:\Downloads", isDesktop: false, isDialog: true);

        Assert.AreEqual(@"C:\Workspace", tracker.LastActiveExplorerPath);
        Assert.AreEqual(@"D:\Downloads", tracker.ActivePath);
    }
}
