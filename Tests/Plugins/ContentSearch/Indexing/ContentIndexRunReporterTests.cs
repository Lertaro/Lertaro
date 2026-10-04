using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk notification hook, so it must not run concurrently with
// anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexRunReporterTests
{
    private readonly List<NotificationRequest> _requests = new();

    [TestInitialize]
    public void CaptureNotifications()
    {
        _requests.Clear();
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = (request, _) =>
        {
            _requests.Add(request);
            return null;
        };
    }

    [TestCleanup]
    public void ReleaseNotifications() => PluginSdk.Services.PluginNotificationService.ShowRequestFunc = null;

    [TestMethod]
    public void Observe_EmptyQueueWithNoRunOpen_SaysNothing()
    {
        // The worker loop passes through an idle queue constantly; only a queue that drained after
        // holding files ends a run.
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: false, indexedFiles: 0);

        Assert.IsEmpty(_requests);
    }

    [TestMethod]
    public void Observe_QueueStillBusy_SaysNothingForEveryBatchOfTheRun()
    {
        // A run of thousands of files is dozens of batches; the user gets one card at the end, not
        // one every twenty-five files.
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 0);
        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 25);
        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 50);

        Assert.IsEmpty(_requests, "a run reports when it ends, never while it is still working");
    }

    [TestMethod]
    public void Observe_DrainedQueue_ReportsCompletionOnce()
    {
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 0);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: false, indexedFiles: 12);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: false, indexedFiles: 12);

        Assert.HasCount(1, _requests, "the summary belongs to the run that ended, not to every later idle pass");
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexFinishedTitle"), _requests[0].Title);
        Assert.Contains("12", _requests[0].Message, "the summary counts what became searchable");
    }

    [TestMethod]
    public void Observe_CancelledRun_ReportsInterruption()
    {
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 0);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: true, indexedFiles: 5);

        Assert.HasCount(1, _requests);
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexStoppedTitle"), _requests[0].Title);
    }

    [TestMethod]
    public void Observe_CancelledRunAtTheCap_ReportsWhyItGaveUp()
    {
        // Both endings truncate the run, and they are not the same news: the cap one asks the user to
        // raise a setting, the other is the plugin being switched off under them.
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: true, pausedAtCap: true, wasCancelled: false, indexedFiles: 0);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: true, wasCancelled: true, indexedFiles: 9);

        Assert.HasCount(1, _requests);
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexPausedTitle"), _requests[0].Title);
        Assert.AreEqual(NotificationLevel.Info, _requests[0].Level, "the cap pause already warned; the ending only informs");
    }

    [TestMethod]
    public void Observe_CancelledWithAnIdleQueue_SaysNothing()
    {
        // The scheduler reports once after its loop with wasCancelled set by the stop token alone; a
        // worker stopped between runs must not announce an interruption of work nobody queued.
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: true, indexedFiles: 3);

        Assert.IsEmpty(_requests);
    }

    [TestMethod]
    public void Observe_SecondRunAfterAReportedOne_ReportsAgain()
    {
        // A watcher-triggered scan starts a new run; the summary must not be a once-per-session event.
        var reporter = new ContentIndexRunReporter();

        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 0);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: false, indexedFiles: 4);
        reporter.Observe(hasPendingFiles: true, pausedAtCap: false, wasCancelled: false, indexedFiles: 4);
        reporter.Observe(hasPendingFiles: false, pausedAtCap: false, wasCancelled: false, indexedFiles: 8);

        Assert.HasCount(2, _requests);
    }

    // No host runs in the test process, so TranslationService.LookupFunc keeps its default and returns
    // the key in brackets; asserting on that proves the intended key was asked for without hardcoding
    // a sentence the tests would then have to keep in step with the JSON files.
    private static string TranslationKey(string key) => $"[{key}]";
}
