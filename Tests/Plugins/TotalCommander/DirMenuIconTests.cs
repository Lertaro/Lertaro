using Lertaro.Plugins.TotalCommander.DirMenu;

namespace Lertaro.Plugins.TotalCommander.Tests;

// Reached from the quick-navigation submenu loader's Task.Run and from the UI thread's action-menu build at
// the same time, which is why the render no longer happens while holding the cache lock (a caller waiting on
// the UI thread from inside the lock would deadlock against a UI caller waiting on the lock). A test can pin
// down that a background caller still gets real handles and that the cache still hands out one per glyph.
[TestClass]
[DoNotParallelize]
public sealed class DirMenuIconTests
{
    [TestCleanup]
    public void Cleanup() => DirMenuIcon.Invalidate();

    [TestMethod]
    public async Task GetRootHBitmap_FromABackgroundThread_ProducesAHandle()
    {
        var handle = await Task.Run(DirMenuIcon.GetRootHBitmap);

        Assert.AreNotEqual(IntPtr.Zero, handle);
    }

    [TestMethod]
    public async Task GetMenuGroupHBitmap_CalledAgain_ReturnsTheSameCachedHandle()
    {
        var first = await Task.Run(DirMenuIcon.GetMenuGroupHBitmap);
        var second = await Task.Run(DirMenuIcon.GetMenuGroupHBitmap);

        Assert.AreNotEqual(IntPtr.Zero, first);
        Assert.AreEqual(first, second);
    }
}
