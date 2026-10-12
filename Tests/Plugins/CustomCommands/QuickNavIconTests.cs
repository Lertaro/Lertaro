using Lertaro.Plugins.CustomCommands;

namespace Lertaro.Plugins.CustomCommands.Tests;

// The quick-navigation submenu loader builds its rows inside Task.Run, so these getters are reached from a
// pool thread while the UI thread may be building the action menu at the same time. The render is no longer
// performed while holding the cache lock for that reason (a caller waiting on the UI thread from inside the
// lock would deadlock against a UI caller waiting on the lock); what a test can pin down is that a background
// caller still gets real handles, and that the cache still hands out one handle per glyph.
[TestClass]
[DoNotParallelize]
public sealed class QuickNavIconTests
{
    [TestCleanup]
    public void Cleanup() => QuickNavIcon.Invalidate();

    [TestMethod]
    public async Task GetCommandHBitmap_FromABackgroundThread_ProducesAHandle()
    {
        var handle = await Task.Run(() => QuickNavIcon.GetCommandHBitmap(null));

        Assert.AreNotEqual(IntPtr.Zero, handle);
    }

    [TestMethod]
    public async Task GetCommandHBitmap_CalledAgain_ReturnsTheSameCachedHandle()
    {
        var first = await Task.Run(() => QuickNavIcon.GetCommandHBitmap(null));
        var second = await Task.Run(() => QuickNavIcon.GetCommandHBitmap(null));

        Assert.AreNotEqual(IntPtr.Zero, first);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task GetCategoryHBitmap_FromABackgroundThread_ProducesAHandle()
    {
        var handle = await Task.Run(QuickNavIcon.GetCategoryHBitmap);

        Assert.AreNotEqual(IntPtr.Zero, handle);
    }
}
