using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger and notification hooks, so it must not run concurrently
// with anything that reads or resets them.
[TestClass]
[DoNotParallelize]
public sealed class IndexCapPauseMonitorTests
{
    private readonly List<string> _logLines = new();
    private readonly List<NotificationRequest> _notifications = new();

    [TestInitialize]
    public void SetUp()
    {
        _logLines.Clear();
        _notifications.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = (request, _) =>
        {
            _notifications.Add(request);
            return null;
        };
    }

    [TestCleanup]
    public void TearDown()
    {
        PluginSdk.Logger.LogAction = null;
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = null;
    }

    [TestMethod]
    public void IsPaused_UnderTheCap_StaysUnpausedAndSilent()
    {
        var monitor = new IndexCapPauseMonitor();

        Assert.IsFalse(monitor.IsPaused(1024, 2048));
        Assert.IsEmpty(_logLines);
        Assert.IsEmpty(_notifications, "nothing is wrong below the cap, so the user hears nothing");
    }

    [TestMethod]
    public void IsPaused_OverTheCap_ReportsOnceAcrossRepeatedBatches()
    {
        // A whole scan re-enqueues the skipped files batch after batch: the diagnostic has to name
        // the pause, not repeat itself per batch for as long as the cap holds. The user-facing card
        // is edge-triggered by the same episode, so it cannot turn into a stream of warnings either.
        var monitor = new IndexCapPauseMonitor();

        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsTrue(monitor.IsPaused(8192, 2048), "still over the cap: still the same episode");

        Assert.HasCount(1, _logLines, $"[{string.Join("; ", _logLines)}]");
        Assert.Contains("indexing is paused until the cap is raised or the index cleared", _logLines[0]);
        Assert.HasCount(1, _notifications, "one card per paused episode, not per batch");
        Assert.AreEqual(NotificationLevel.Warn, _notifications[0].Level);
    }

    [TestMethod]
    public void IsPaused_CapRaisedAndReachedAgain_ReportsTheNewEpisode()
    {
        var monitor = new IndexCapPauseMonitor();

        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsFalse(monitor.IsPaused(1024, 2048), "back under the cap: the episode ended");
        Assert.IsTrue(monitor.IsPaused(4096, 2048), "reaching the cap again is a new episode");

        Assert.HasCount(2, _logLines, $"[{string.Join("; ", _logLines)}]");
        Assert.HasCount(2, _notifications, "the user is told again only because they let it fill up again");
    }

    [TestMethod]
    public void IsPauseReported_TracksWhetherTheReportedPauseIsStillInForce()
    {
        // The scheduler reads this to name the reason a run ended: reaching the cap is the one ending
        // the user has to act on, and it is not the same as being interrupted by a stop.
        var monitor = new IndexCapPauseMonitor();

        Assert.IsFalse(monitor.IsPauseReported);
        monitor.IsPaused(4096, 2048);
        Assert.IsTrue(monitor.IsPauseReported);
        monitor.IsPaused(1024, 2048);
        Assert.IsFalse(monitor.IsPauseReported, "back under the cap the pause is over");
    }
}
