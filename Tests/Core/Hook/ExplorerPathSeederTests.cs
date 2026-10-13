using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

// The seeder reads a folder off the hook-install thread and then publishes it into the tracker's state. A
// tracker that has already stopped has no state to publish into -- the App mirror that state feeds is leaving
// with it -- so the read that comes back late is dropped rather than announced.
[TestClass]
public sealed class ExplorerPathSeederTests
{
    private static readonly IntPtr Window = new(0x1234);

    [TestMethod]
    public void MayPublish_OnlyWhileTheTrackerIsRunningAndThereIsAWindowAndAPath()
    {
        Assert.IsTrue(ExplorerPathSeeder.MayPublish(true, Window, @"D:\Work"));
        Assert.IsFalse(ExplorerPathSeeder.MayPublish(false, Window, @"D:\Work"));
        Assert.IsFalse(ExplorerPathSeeder.MayPublish(true, IntPtr.Zero, @"D:\Work"));
        Assert.IsFalse(ExplorerPathSeeder.MayPublish(true, Window, null));
        Assert.IsFalse(ExplorerPathSeeder.MayPublish(true, Window, string.Empty));
    }
}
