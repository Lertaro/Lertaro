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
}
