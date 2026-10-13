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

    // Callers reaching a cold cache at the same time must still leave with ONE handle. Without the lock both
    // rendered and the later one overwrote the earlier, leaving that first bitmap owned by nothing.
    [TestMethod]
    public async Task GetCategoryHBitmap_CalledConcurrently_HandsOutASingleHandle()
    {
        const int callers = 8;
        using var start = new CountdownEvent(1);
        var renders = Enumerable.Range(0, callers)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return QuickNavIcon.GetCategoryHBitmap();
            }))
            .ToArray();

        start.Signal();
        var handles = await Task.WhenAll(renders);

        Assert.IsFalse(Array.Exists(handles, handle => handle == IntPtr.Zero));
        Assert.AreEqual(1, handles.Distinct().Count());
    }
}
