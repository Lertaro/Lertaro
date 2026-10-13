using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Search;

// The cache is process-wide static state that other tests in this assembly can drop from another thread --
// a settings change and a recorded history entry both invalidate it -- so this class runs on its own.
[TestClass]
[DoNotParallelize]
public sealed class QuickLaunchSourceCacheTests
{
    [TestInitialize]
    public void Reset() => QuickLaunchSourceCache.Invalidate();

    [TestCleanup]
    public void Cleanup() => QuickLaunchSourceCache.Invalidate();

    [TestMethod]
    public void TryGet_AnswerJustStored_ComesBackAsTheSameList()
    {
        var items = Results(@"C:\one");

        QuickLaunchSourceCache.Set("source", items, now: 1_000);

        Assert.IsTrue(QuickLaunchSourceCache.TryGet("source", now: 1_000, out var cached));
        Assert.AreSame(items, cached);
    }

    [TestMethod]
    public void TryGet_NothingWasStored_IsAMiss()
        => Assert.IsFalse(QuickLaunchSourceCache.TryGet("source", now: 1_000, out _));

    [TestMethod]
    public void TryGet_AnotherSourcesAnswer_IsNotHandedOver()
    {
        QuickLaunchSourceCache.Set("first", Results(@"C:\one"), now: 1_000);

        Assert.IsFalse(QuickLaunchSourceCache.TryGet("second", now: 1_000, out _));
    }

    // The lifetime is what makes a stale list eventually re-read, so it has to expire on its own.
    [TestMethod]
    public void TryGet_LongAfterItWasStored_IsAMiss()
    {
        QuickLaunchSourceCache.Set("source", Results(@"C:\one"), now: 1_000);

        var anHourLater = 1_000 + (long)TimeSpan.FromHours(1).TotalMilliseconds;

        Assert.IsFalse(QuickLaunchSourceCache.TryGet("source", anHourLater, out _));
    }

    [TestMethod]
    public void Invalidate_DropsWhatWasStored()
    {
        QuickLaunchSourceCache.Set("source", Results(@"C:\one"), now: 1_000);

        QuickLaunchSourceCache.Invalidate();

        Assert.IsFalse(QuickLaunchSourceCache.TryGet("source", now: 1_000, out _));
    }

    [TestMethod]
    public void Set_ReplacesWhatWasStoredBefore()
    {
        var first = Results(@"C:\one");
        var second = Results(@"C:\two");

        QuickLaunchSourceCache.Set("source", first, now: 1_000);
        QuickLaunchSourceCache.Set("source", second, now: 1_000);

        Assert.IsTrue(QuickLaunchSourceCache.TryGet("source", now: 1_000, out var cached));
        Assert.AreSame(second, cached);
    }

    private static IReadOnlyList<ISearchResult> Results(string path)
        => new[] { new SimpleSearchResult { Name = path, FullPath = path } };
}
