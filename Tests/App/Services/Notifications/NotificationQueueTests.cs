using Lertaro.App.Services.Notifications;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Services.Notifications;

[TestClass]
public class NotificationQueueTests
{
    private const string PluginA = "Lertaro.Plugins.Alpha";
    private const string PluginB = "Lertaro.Plugins.Beta";
    private const string SourceA = "Alpha";
    private const string SourceB = "Beta";

    private readonly FakeScreen _screen = new();
    private NotificationQueue _queue = null!;

    [TestInitialize]
    public void BuildQueue() =>
        _queue = new NotificationQueue(
            () => _screen.Fullscreen,
            item => _screen.Shown.Add(item),
            (item, fade) =>
            {
                _screen.Hidden.Add(item);
                if (fade) _screen.FadedOut.Add(item);
            },
            message => _screen.Warnings.Add(message));

    [TestMethod]
    public void ClipDuration_LandsOnTheNearestBoundAndSuppliesThePositionDefault()
    {
        Assert.AreEqual(NotificationQueue.CardDefaultSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, null));
        Assert.AreEqual(NotificationQueue.NoticeDefaultSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.BottomNotice, null));

        // Zero is the card's lower bound rather than its default: the default is what null means.
        Assert.AreEqual(NotificationQueue.CardMinSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, 0));
        Assert.AreEqual(NotificationQueue.CardMinSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, -5));
        Assert.AreEqual(NotificationQueue.CardMaxSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, 300));
        Assert.AreEqual(NotificationQueue.CardMaxSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, 30));
        Assert.AreEqual(NotificationQueue.NoticeMaxSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.BottomNotice, 60));
        Assert.AreEqual(9, NotificationQueue.ClipDuration(NotificationPosition.BottomNotice, 9));

        // A value that cannot be compared asked for a duration, so it lands on the lower bound rather than
        // outliving every notification that was given a real one.
        Assert.AreEqual(NotificationQueue.CardMinSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.CardStack, double.NaN));
        Assert.AreEqual(NotificationQueue.NoticeMinSeconds,
            NotificationQueue.ClipDuration(NotificationPosition.BottomNotice, double.NaN));
    }

    [TestMethod]
    public void EmptyTitleAndMessage_FailWithoutShowingAnything()
    {
        var empty = _queue.Submit(new NotificationRequest { Title = "  ", Message = string.Empty }, PluginA, SourceA);

        Assert.IsFalse(ResultOf(empty).Succeeded);
        Assert.AreEqual(NotificationFailure.InvalidRequest, ResultOf(empty).Failure);
        Assert.HasCount(0, _screen.Shown);
    }

    [TestMethod]
    public void TitleOnly_IsShownAsUsual()
    {
        var card = _queue.Submit(new NotificationRequest { Title = "Rebuilt the index" }, PluginA, SourceA);

        Assert.IsTrue(IsOutstanding(card));
        Assert.HasCount(1, _screen.Shown);
    }

    [TestMethod]
    public void CardStack_CapsWhatIsVisibleAtFiveAndRefillsInArrivalOrder()
    {
        var shown = ShowCards(1, 5);

        Assert.HasCount(5, _screen.Shown);
        Assert.AreEqual(shown[0], _screen.Shown[0]);

        var queued = ShowCard("card 6");
        var overflow = ShowCard("card 7");
        Assert.HasCount(5, _screen.Shown);

        _queue.NotifyClosed(shown[0]);
        _queue.NotifyClosed(shown[1]);

        Assert.AreEqual(queued, _screen.Shown[5]);
        Assert.AreEqual(overflow, _screen.Shown[6]);
        Assert.AreEqual(NotificationResult.Success, ResultOf(shown[0]));
        Assert.AreEqual(NotificationResult.Success, ResultOf(shown[1]));
    }

    [TestMethod]
    public void SameIdCard_ReplacesTheVisibleOneAndEndsItAsReplaced()
    {
        var first = ShowCard("download started", id: "job-1");
        var second = ShowCard("download finished", id: "job-1");

        Assert.AreEqual(NotificationFailure.Replaced, ResultOf(first).Failure);
        // The screen still shows one card, not two: the replacement is why the visible limit cannot be
        // worked around by repeating an Id.
        Assert.HasCount(1, _screen.Visible);
        Assert.AreEqual(second, _screen.Shown[1]);
        Assert.AreEqual(first, _screen.Hidden[0]);
        // A replacement paints over the old card rather than fading it out first.
        CollectionAssert.DoesNotContain(_screen.FadedOut, first);
    }

    [TestMethod]
    public void SameIdCard_QueuesBehindTheVisibleOnesRatherThanJumpingTheLimit()
    {
        // An Id nobody is showing yet is not a reason to squeeze past the five visible cards, and not a
        // reason to replace a queued copy either: the queue rules only touch what is on screen.
        var shown = ShowCards(1, 5);
        var queued = ShowCard("queued copy", id: "later");
        var arriving = ShowCard("arrives later", id: "later");

        Assert.HasCount(5, _screen.Shown);
        Assert.IsTrue(IsOutstanding(queued));
        Assert.IsTrue(IsOutstanding(arriving));

        _queue.NotifyClosed(shown[0]);
        Assert.AreEqual(queued, _screen.Shown[5]);
        Assert.IsTrue(IsOutstanding(arriving));
    }

    [TestMethod]
    public void SixthQueuedCardForOnePlugin_FailsQueueFullWithAWarning()
    {
        ShowCards(1, 10);

        var overflow = ShowCard("card 11");

        Assert.AreEqual(NotificationFailure.QueueFull, ResultOf(overflow).Failure);
        Assert.HasCount(1, _screen.Warnings);
        StringAssert.Contains(_screen.Warnings[0], "QueueFull");
        StringAssert.Contains(_screen.Warnings[0], "card 11");
    }

    [TestMethod]
    public void AnotherPlugin_KeepsItsOwnQueueWhenOnePluginIsFull()
    {
        ShowCards(1, 10);

        var beta = ShowCard("beta 0", seconds: 6, plugin: PluginB, source: SourceB);

        Assert.IsTrue(IsOutstanding(beta));
        Assert.AreEqual(6, beta.DurationSeconds);
        Assert.IsFalse(_screen.Warnings.Any(warning => warning.Contains("QueueFull", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Notice_AlwaysReplacesWhatIsShowing_AndNeverReportsQueueFull()
    {
        var notices = Enumerable.Range(1, 8).Select(i => ShowNotice($"notice {i}")).ToArray();

        Assert.HasCount(8, _screen.Shown);
        Assert.HasCount(7, _screen.Hidden);
        for (var i = 0; i < 7; i++)
        {
            Assert.AreEqual(NotificationFailure.Replaced, ResultOf(notices[i]).Failure);
            Assert.AreEqual(notices[i], _screen.Hidden[i]);
        }
        Assert.IsTrue(IsOutstanding(notices[7]));
        Assert.HasCount(1, _screen.Visible);
        Assert.IsFalse(_screen.Warnings.Any(warning => warning.Contains("QueueFull", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CardRequestedWhileFullscreen_CollapsesIntoTheNoticeAndOntoTheTimeCap()
    {
        _screen.Fullscreen = true;

        var collapsed = ShowCard("boss defeated", seconds: 30);

        Assert.AreEqual(NotificationPosition.BottomNotice, collapsed.EffectivePosition);
        Assert.AreEqual(NotificationQueue.CollapsedCardMaxSeconds, collapsed.DurationSeconds);
        Assert.AreEqual(collapsed, _screen.Shown[0]);
    }

    [TestMethod]
    public void Collapse_LogsWhatTheNoticeLineCouldNotShow()
    {
        _screen.Fullscreen = true;

        _queue.Submit(new NotificationRequest { Title = "Sync finished", Message = "42 files moved", DurationSeconds = 20 },
            PluginA, SourceA);

        Assert.HasCount(1, _screen.Warnings);
        StringAssert.Contains(_screen.Warnings[0], "Sync finished");
        StringAssert.Contains(_screen.Warnings[0], "42 files moved");
        StringAssert.Contains(_screen.Warnings[0], SourceA);
    }

    [TestMethod]
    public void NoticeReplacedBeforeItsTimeUp_LogsTheLineNobodyFinishedReading()
    {
        var first = ShowNotice("first line");
        ShowNotice("second line");

        Assert.IsFalse(IsOutstanding(first));
        Assert.HasCount(1, _screen.Warnings);
        StringAssert.Contains(_screen.Warnings[0], "first line");
    }

    [TestMethod]
    public void QueuedCard_IsReJudgedForFullscreenWhenItLeavesTheQueue()
    {
        var shown = ShowCards(1, 5);
        var queued = ShowCard("queued while fullscreen starts");

        // The screen went busy after the request was accepted, so the queue has to notice at display time.
        _screen.Fullscreen = true;
        _queue.NotifyClosed(shown[0]);

        Assert.AreEqual(NotificationPosition.BottomNotice, queued.EffectivePosition);
        Assert.AreEqual(NotificationQueue.CollapsedCardMaxSeconds, queued.DurationSeconds);
    }

    [TestMethod]
    public void QueuedCards_GoThroughTheNoticeLineOneAtATimeWhenTheScreenIsBusy()
    {
        var shown = ShowCards(1, 5);
        var queued = ShowCards(6, 8);
        _screen.Fullscreen = true;

        _queue.NotifyClosed(shown[0]);

        // Feeding all three at once would have each replace the one before it before anyone could read it.
        Assert.HasCount(6, _screen.Shown);
        Assert.AreEqual(queued[0], _screen.Shown[^1]);

        _queue.NotifyClosed(_screen.Shown[^1]);
        Assert.AreEqual(queued[1], _screen.Shown[^1]);
        Assert.AreEqual(queued[0], _screen.Shown[^2]);
    }

    [TestMethod]
    public void CancelPlugin_EndsItsShownAndQueuedRequestsAndLeavesOthersAlone()
    {
        var alphaVisible = ShowCard("alpha visible");
        ShowCards(2, 5);
        var alphaQueued = ShowCard("alpha queued");
        var betaVisible = ShowCard("beta visible", plugin: PluginB, source: SourceB);

        _queue.CancelPlugin(PluginA);

        Assert.AreEqual(NotificationFailure.CancelledByPluginUnload, ResultOf(alphaVisible).Failure);
        Assert.AreEqual(NotificationFailure.CancelledByPluginUnload, ResultOf(alphaQueued).Failure);
        Assert.IsTrue(IsOutstanding(betaVisible));
        CollectionAssert.Contains(_screen.Hidden, alphaVisible);
        CollectionAssert.DoesNotContain(_screen.Hidden, betaVisible);
        // A request that never reached the screen has no window to take down.
        CollectionAssert.DoesNotContain(_screen.Hidden, alphaQueued);
    }

    [TestMethod]
    public void CancelPlugin_RefillsTheFreedSlotFromAnotherPlugin()
    {
        var shown = ShowCards(1, 5);
        var beta = ShowCard("beta waiting", plugin: PluginB, source: SourceB);

        _queue.CancelPlugin(PluginA);

        Assert.IsTrue(IsOutstanding(beta));
        Assert.AreEqual(beta, _screen.Shown[^1]);
        Assert.AreEqual(NotificationFailure.CancelledByPluginUnload, ResultOf(shown[0]).Failure);
    }

    [TestMethod]
    public void Shutdown_EndsEverythingWithHostShuttingDown()
    {
        var card = ShowCard("still running");
        ShowCards(2, 5);
        var queued = ShowCard("never shown");
        var notice = ShowNotice("bottom line");

        _queue.Shutdown();

        foreach (var item in new[] { card, queued, notice })
        {
            Assert.AreEqual(NotificationFailure.HostShuttingDown, ResultOf(item).Failure);
        }
    }

    [TestMethod]
    public void DismissFromTheHandle_EndsAQueuedRequestWithoutShowingIt()
    {
        var shown = ShowCards(1, 5);
        var queued = ShowCard("withdrawn");

        queued.Dismiss();

        Assert.AreEqual(NotificationResult.Success, ResultOf(queued));
        CollectionAssert.DoesNotContain(_screen.Shown, queued);
        Assert.HasCount(0, _screen.Hidden);

        _queue.NotifyClosed(shown[0]);
        Assert.HasCount(5, _screen.Shown);
    }

    [TestMethod]
    public void DismissOfAShownCard_TakesItsWindowDown()
    {
        var card = ShowCard("withdrawn by its caller");
        var hiddenBefore = _screen.Hidden.Count;

        card.Dismiss();

        Assert.AreEqual(NotificationResult.Success, ResultOf(card));
        // Ending a notification that is on screen has to reach the window too. Deciding the queue's side
        // only left a card nobody owned sitting there until its original duration ran out.
        Assert.HasCount(hiddenBefore + 1, _screen.Hidden);
        CollectionAssert.Contains(_screen.FadedOut, card);
    }

    [TestMethod]
    public void WithdrawingAnAlreadyReplacedNotice_LeavesTheNewOneAlone()
    {
        var first = ShowNotice("withdrawn after the fact");
        var replacement = ShowNotice("the line now showing");

        first.Dismiss();

        Assert.AreEqual(NotificationFailure.Replaced, ResultOf(first).Failure);
        Assert.IsTrue(IsOutstanding(replacement));
        CollectionAssert.DoesNotContain(_screen.FadedOut, replacement);
    }

    [TestMethod]
    public void EveryRequestThatWasAccepted_ReachesAnEndState()
    {
        var items = Enumerable.Range(0, 24)
            .Select(i => i % 3 == 0
                ? ShowNotice($"notice {i}")
                : ShowCard($"card {i}", id: i % 5 == 0 ? $"id-{i}" : null, plugin: i % 2 == 0 ? PluginA : PluginB))
            .ToArray();

        _queue.Shutdown();

        foreach (var item in items)
        {
            Assert.IsTrue(item.Completion.IsCompleted, $"nothing ever decided the end of \"{item.Request.Message}\"");
        }
    }

    private NotificationItem ShowCard(string text, string? id = null, double? seconds = null,
        string plugin = PluginA, string source = SourceA) =>
        _queue.Submit(new NotificationRequest { Title = "title", Message = text, Id = id, DurationSeconds = seconds },
            plugin, source);

    private NotificationItem ShowNotice(string text, string plugin = PluginA) =>
        _queue.Submit(new NotificationRequest { Message = text, Position = NotificationPosition.BottomNotice },
            plugin, SourceA);

    /// <summary>Shows the card numbers from the lower bound up to and including the upper one.</summary>
    private NotificationItem[] ShowCards(int first, int last, string plugin = PluginA, string source = SourceA) =>
        Enumerable.Range(first, last - first + 1)
            .Select(i => ShowCard($"card {i}", plugin: plugin, source: source))
            .ToArray();

    /// <summary>True while the notification has not reached its end state, which is what an accepted
    /// request looks like before it has been shown and closed.</summary>
    private static bool IsOutstanding(NotificationItem item) => !item.Completion.IsCompleted;

    private static NotificationResult ResultOf(NotificationItem item)
    {
        Assert.IsTrue(item.Completion.IsCompleted, $"the caller is still waiting on \"{item.Request.Message}\"");
        return item.Completion.Result;
    }

    private sealed class FakeScreen
    {
        public List<NotificationItem> Shown { get; } = [];
        public List<NotificationItem> Hidden { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool Fullscreen { get; set; }

        /// <summary>What a real screen would still be showing: presented and not taken down again.</summary>
        public List<NotificationItem> Visible => Shown.Where(item => !Hidden.Contains(item)).ToList();

        /// <summary>The ones taken down politely, as opposed to the replacements and cancellations that had
        /// to go immediately.</summary>
        public List<NotificationItem> FadedOut { get; } = [];
    }
}
