using Lertaro.Plugins.FolderCascader.Navigation;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.FolderCascader.Tests;

// The icon build is reached from a background thread in production: QuickNavigationSubMenuLoader runs
// provider.GetMenuItems inside Task.Run (so a slow provider cannot freeze the app), and MenuBuilder calls
// IconBitmapCache.EnsureIcons on the way in. Every Freezable it builds is thread-affine, so building the
// brush on one thread and rendering on another throws "the DependencyObject belongs to a different thread
// than its parent Freezable" -- issue #350's repeated warning -- and EnsureIcons swallows it, leaving every
// handle at zero. These tests therefore assert the handles, not the absence of a throw, and they assert the
// warning was not logged, because a swallowed failure is exactly what the report had to work with.
[TestClass]
[DoNotParallelize]
public sealed class IconBitmapCacheTests
{
    private Action<string, LogLevel>? _previousLogAction;
    private readonly List<string> _messages = [];

    [TestInitialize]
    public void CaptureLog()
    {
        _previousLogAction = Logger.LogAction;
        Logger.LogAction = (message, _) =>
        {
            lock (_messages) _messages.Add(message);
        };
    }

    [TestCleanup]
    public void RestoreLog() => Logger.LogAction = _previousLogAction;

    [TestMethod]
    public async Task EnsureIcons_FromABackgroundThread_BuildsEveryHandleWithoutWarning()
    {
        // Task.Run is the production shape: a pool thread, not this test's own.
        await Task.Run(IconBitmapCache.EnsureIcons);

        AssertNoIconWarning();
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.FavoritesHBitmap);
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.HistoryHBitmap);
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.OpenedFoldersHBitmap);
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.CategoryHBitmap);
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.AddHBitmap);
    }

    // Called twice in a row from different threads: the second build must replace the first (the method
    // always re-renders so a theme change is picked up) without the old handles or the lock going wrong.
    [TestMethod]
    public async Task EnsureIcons_TwiceFromDifferentThreads_StillBuildsEveryHandle()
    {
        IconBitmapCache.EnsureIcons();
        await Task.Run(IconBitmapCache.EnsureIcons);

        AssertNoIconWarning();
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.FavoritesHBitmap);
        Assert.AreNotEqual(IntPtr.Zero, IconBitmapCache.AddHBitmap);
    }

    private void AssertNoIconWarning()
    {
        lock (_messages)
            Assert.IsFalse(
                _messages.Exists(message => message.Contains("Failed to update themed icons", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, _messages));
    }
}
