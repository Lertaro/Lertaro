using Lertaro.PluginSdk.Services;

namespace Lertaro.Core.Tests.Services.Plugin.DirectoryIndex;

// Subscribing to your own registrations. The point of the per-registrant shape is that a subscriber
// never has to ask "was that mine" -- so what is pinned here is that it genuinely never hears about
// anybody else's, because the moment it does, every subscriber needs that check back.
[TestClass]
public sealed class DirectoryIndexerServiceWatchTests
{
    [TestMethod]
    public void AChangeReachesOnlyTheRegistrantItBelongsTo()
    {
        var mine = 0;
        var theirs = 0;
        using var a = DirectoryIndexerService.WatchDirectories("plugin-a", () => mine++);
        using var b = DirectoryIndexerService.WatchDirectories("plugin-b", () => theirs++);

        DirectoryIndexerService.NotifyDirectoryChanged("plugin-a");

        Assert.AreEqual(1, mine);
        Assert.AreEqual(0, theirs);
    }

    [TestMethod]
    public void RegistrationIdsAreMatchedWithoutCase()
    {
        var calls = 0;
        using var watch = DirectoryIndexerService.WatchDirectories("Plugin-Case", () => calls++);

        DirectoryIndexerService.NotifyDirectoryChanged("plugin-case");

        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DisposingStopsTheCallsWithoutDisturbingTheOthers()
    {
        var first = 0;
        var second = 0;
        var watch = DirectoryIndexerService.WatchDirectories("plugin-shared", () => first++);
        using var kept = DirectoryIndexerService.WatchDirectories("plugin-shared", () => second++);

        watch.Dispose();
        DirectoryIndexerService.NotifyDirectoryChanged("plugin-shared");

        Assert.AreEqual(0, first);
        Assert.AreEqual(1, second);
    }

    [TestMethod]
    public void DisposingTwiceDoesNotRemoveSomebodyElsesHandler()
    {
        var calls = 0;
        var watch = DirectoryIndexerService.WatchDirectories("plugin-twice", () => calls++);
        watch.Dispose();
        watch.Dispose();

        using var replacement = DirectoryIndexerService.WatchDirectories("plugin-twice", () => calls++);
        DirectoryIndexerService.NotifyDirectoryChanged("plugin-twice");

        Assert.AreEqual(1, calls);
    }

    // A plugin being torn down is exactly when its handler is most likely to unsubscribe from inside
    // its own callback, and a list mutated while it is being walked would throw into whatever thread
    // the notification arrived on.
    [TestMethod]
    public void AHandlerMayUnsubscribeItselfWhileRunning()
    {
        IDisposable? watch = null;
        var calls = 0;
        watch = DirectoryIndexerService.WatchDirectories("plugin-selfremove", () =>
        {
            calls++;
            watch!.Dispose();
        });

        DirectoryIndexerService.NotifyDirectoryChanged("plugin-selfremove");
        DirectoryIndexerService.NotifyDirectoryChanged("plugin-selfremove");

        Assert.AreEqual(1, calls);
    }

    // One plugin's bad handler is its own problem: the others registered for the same change still run.
    [TestMethod]
    public void AThrowingHandlerDoesNotStopTheRest()
    {
        var reached = false;
        using var bad = DirectoryIndexerService.WatchDirectories("plugin-throws", () => throw new InvalidOperationException("boom"));
        using var good = DirectoryIndexerService.WatchDirectories("plugin-throws", () => reached = true);

        DirectoryIndexerService.NotifyDirectoryChanged("plugin-throws");

        Assert.IsTrue(reached);
    }

    [TestMethod]
    public void NotifyingSomethingNobodyWatchesIsHarmless()
        => DirectoryIndexerService.NotifyDirectoryChanged("plugin-nobody-registered");
}
