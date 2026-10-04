using System.Reflection;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk notification and Logger hooks, so it must not run concurrently
// with anything that reads or resets them.
[TestClass]
[DoNotParallelize]
public sealed class ContentIndexNotifierTests
{
    private readonly List<NotificationRequest> _requests = new();
    private readonly List<Assembly> _sources = new();
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void CaptureNotifications()
    {
        _requests.Clear();
        _sources.Clear();
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = (request, source) =>
        {
            _requests.Add(request);
            _sources.Add(source);
            return null;
        };
    }

    [TestCleanup]
    public void ReleaseNotifications()
    {
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = null;
        PluginSdk.Logger.LogAction = null;
    }

    [TestMethod]
    public void NotifyIndexCapReached_WarnsWithThePauseAndHowToLiftIt()
    {
        ContentIndexNotifier.NotifyIndexCapReached();

        Assert.HasCount(1, _requests, $"exactly one card per paused episode: [{Describe()}]");
        var request = _requests[0];
        Assert.AreEqual(NotificationLevel.Warn, request.Level, "the cap pause is the one index problem the user must act on");
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationCapReachedTitle"), request.Title);
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationCapReachedMessage"), request.Message);
    }

    [TestMethod]
    public void NotifyRunFinished_CompletedEnumeratesTheSearchableFiles()
    {
        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.Completed, 42);

        Assert.HasCount(1, _requests);
        Assert.AreEqual(NotificationLevel.Info, _requests[0].Level, "a finished run is news, not a warning");
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexFinishedTitle"), _requests[0].Title);
        Assert.AreEqual(TranslationKeyWith("ContentSearch_NotificationIndexFinishedMessage", 42), _requests[0].Message);
    }

    [TestMethod]
    public void NotifyRunFinished_InterruptedSaysSoWithoutWarning()
    {
        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.Interrupted, 7);

        Assert.HasCount(1, _requests);
        Assert.AreEqual(NotificationLevel.Info, _requests[0].Level, "an interrupted run is not an error to shout about");
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexStoppedTitle"), _requests[0].Title);
        Assert.AreEqual(TranslationKeyWith("ContentSearch_NotificationIndexStoppedMessage", 7), _requests[0].Message);
    }

    [TestMethod]
    public void NotifyRunFinished_GaveUpNamesTheCapAsTheReason()
    {
        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.GaveUp, 11);

        Assert.HasCount(1, _requests);
        Assert.AreEqual(NotificationLevel.Info, _requests[0].Level, "the cap pause already warned; the ending only informs");
        Assert.AreEqual(TranslationKey("ContentSearch_NotificationIndexPausedTitle"), _requests[0].Title);
        Assert.AreEqual(TranslationKeyWith("ContentSearch_NotificationIndexPausedMessage", 11), _requests[0].Message);
    }

    [TestMethod]
    public void NotifyRunFinished_HostRefusesTheNotification_DoesNotThrow()
    {
        // The SDK never throws, but the delegate belongs to the host: an escaping throw here would
        // land in the middle of the scheduler's worker loop.
        PluginSdk.Services.PluginNotificationService.ShowRequestFunc = (_, _) => throw new InvalidOperationException("host is going away");

        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.Completed, 1);

        Assert.HasCount(1, _logLines, $"the refusal is logged, not propagated: [{string.Join("; ", _logLines)}]");
        Assert.Contains("Could not show", _logLines[0]);
    }

    [TestMethod]
    public void NotifyRunFinished_NamesThisPluginAsTheSource()
    {
        // Attribution comes from the calling assembly, so the card must carry this plugin's own,
        // which is what the host maps to its display name on screen.
        ContentIndexNotifier.NotifyRunFinished(ContentIndexRunOutcome.Completed, 3);

        Assert.HasCount(1, _sources);
        Assert.AreEqual(typeof(ContentIndexNotifier).Assembly, _sources[0]);
    }

    // No host runs in the test process, so TranslationService.LookupFunc keeps its default and returns
    // the key in brackets; asserting on that proves the intended key was asked for without hardcoding
    // a sentence the tests would then have to keep in step with the JSON files.
    private static string TranslationKey(string key) => $"[{key}]";

    private static string TranslationKeyWith(string key, int value) => string.Format($"[{key}]", value);

    private string Describe() => string.Join("; ", _requests.Select(r => $"{r.Level}:{r.Title}"));
}
