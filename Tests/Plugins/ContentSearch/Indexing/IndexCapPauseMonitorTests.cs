using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run concurrently with
// anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexCapPauseMonitorTests
{
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void TearDown() => PluginSdk.Logger.LogAction = null;

    [TestMethod]
    public void IsPaused_UnderTheCap_StaysUnpausedAndSilent()
    {
        var monitor = new IndexCapPauseMonitor();

        Assert.IsFalse(monitor.IsPaused(1024, 2048));
        Assert.IsEmpty(_logLines);
    }

    [TestMethod]
    public void IsPaused_OverTheCap_ReportsOnceAcrossRepeatedBatches()
    {
        // A whole scan re-enqueues the skipped files batch after batch: the diagnostic has to name
        // the pause, not repeat itself per batch for as long as the cap holds.
        var monitor = new IndexCapPauseMonitor();

        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsTrue(monitor.IsPaused(8192, 2048), "still over the cap: still the same episode");

        Assert.HasCount(1, _logLines, $"[{string.Join("; ", _logLines)}]");
        Assert.Contains("indexing is paused until the cap is raised or the index cleared", _logLines[0]);
    }

    [TestMethod]
    public void IsPaused_CapRaisedAndReachedAgain_ReportsTheNewEpisode()
    {
        var monitor = new IndexCapPauseMonitor();

        Assert.IsTrue(monitor.IsPaused(4096, 2048));
        Assert.IsFalse(monitor.IsPaused(1024, 2048), "back under the cap: the episode ended");
        Assert.IsTrue(monitor.IsPaused(4096, 2048), "reaching the cap again is a new episode");

        Assert.HasCount(2, _logLines, $"[{string.Join("; ", _logLines)}]");
    }
}
