namespace Lertaro.Plugins.WindowSwitcher.Tests;

[TestClass]
public sealed class WindowThumbnailCacheTests
{
    [TestMethod]
    public void ShouldCapture_NoCachedEntryAndNotPending_ReturnsTrue()
        => Assert.IsTrue(WindowThumbnailCache.ShouldCapture(hasCachedEntry: false, isPending: false, ageMs: long.MaxValue));

    [TestMethod]
    public void ShouldCapture_NoCachedEntryButAlreadyPending_ReturnsFalse()
        // Avoids kicking off a second concurrent PrintWindow capture for the same window while one is
        // already in flight.
        => Assert.IsFalse(WindowThumbnailCache.ShouldCapture(hasCachedEntry: false, isPending: true, ageMs: long.MaxValue));

    [TestMethod]
    public void ShouldCapture_FreshCachedEntry_ReturnsFalse()
        => Assert.IsFalse(WindowThumbnailCache.ShouldCapture(hasCachedEntry: true, isPending: false, ageMs: 500));

    [TestMethod]
    public void ShouldCapture_StaleCachedEntry_ReturnsTrue()
        => Assert.IsTrue(WindowThumbnailCache.ShouldCapture(hasCachedEntry: true, isPending: false, ageMs: 5000));

    [TestMethod]
    public void ShouldCapture_StaleCachedEntryButAlreadyPending_ReturnsFalse()
        => Assert.IsFalse(WindowThumbnailCache.ShouldCapture(hasCachedEntry: true, isPending: true, ageMs: 5000));

    [TestMethod]
    public void GetIconOrRefresh_UnknownWindow_ReturnsZeroWithoutThrowing()
    {
        // A window handle that was never captured (and, being a bogus value, will fail
        // WindowThumbnailCapture.Capture's own GetWindowRect check) -- the very first call must
        // return IntPtr.Zero immediately (cache miss) rather than block on the background capture it
        // kicks off.
        var result = WindowThumbnailCache.GetIconOrRefresh(new IntPtr(0x7FFFFFFF), () => { });

        Assert.AreEqual(IntPtr.Zero, result);
    }
}
