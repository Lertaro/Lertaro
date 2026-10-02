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

    // The queue takes its gate from the host, since a plugin's handle has to take the same lock a submission
    // took. The tests hold the one object the service would have handed over.
    private readonly object _gate = new();
    private readonly FakeScreen _screen = new();
    private NotificationQueue _queue = null!;

    [TestInitialize]
    public void BuildQueue() =>
        _queue = new NotificationQueue(
            () => _screen.ReadScreen(),
            _screen.Show,
            _screen.Hide,
            _screen.Warn,
            _gate);

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
        Pump();

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
    public void BurstOfCards_ArrivesOnePerClockPassRatherThanInOneFrame()
    {
        ShowCards(1, 5);

        // Five requests in one burst used to mean five windows in one frame, which is also five windows going off
        // on the same tick eight seconds later with the whole stack dropping five slots at once. Only an empty
        // screen is served on the spot; the rest wait for the clock, which offers one card per pass.
        Assert.HasCount(1, _screen.Shown);
        Assert.IsTrue(_queue.HasWaiting);

        _queue.Feed();
        _queue.Feed();
        Assert.HasCount(3, _screen.Shown);
        Assert.AreEqual("card 3", _screen.Shown[2].Request.Message);
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
    }

    [TestMethod]
    public void SameIdCard_QueuesBehindTheVisibleOnesRatherThanJumpingTheLimit()
    {
        // An Id nobody is showing yet is not a reason to squeeze past the five visible cards, and not a
        // reason to replace a queued copy either: the queue rules only touch what is on screen.
        var shown = ShowCards(1, 5);
        Pump();
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
    public void SameIdReplacement_KeepsTheSlotTheReplacedCardHad()
    {
        var first = ShowCard("progress 40 percent", id: "job-1");
        var later = ShowCard("something else entirely");
        var replacement = ShowCard("progress 100 percent", id: "job-1");

        // The stack is laid out by arrival order, not by the list's, so a replacement carrying a fresh number
        // lands above a card that arrived after the one it overwrote -- i.e. it jumps to the top.
        Assert.AreEqual(first.Sequence, replacement.Sequence);
        Assert.IsTrue(replacement.Sequence < later.Sequence,
            "\"progress 100 percent\" was placed after a card that arrived after the one it replaced");
    }

    [TestMethod]
    public void EleventhCardForOnePlugin_FailsQueueFullWithAWarning()
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
        Pump();
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
        Pump();
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
    public void Refill_ReadsTheScreenOnceForTheWholePassNotOncePerCard()
    {
        var shown = ShowCards(1, 5);
        ShowCards(6, 8);
        _screen.ScreenReads = 0;

        _queue.NotifyClosed(shown[0]);
        _queue.NotifyClosed(shown[1]);

        // Two closes, two promotions, two reads. The screen cannot change underneath a synchronous refill, and
        // every read is a handful of P/Invokes paid on whatever thread the countdown happened to be on.
        Assert.AreEqual(2, _screen.ScreenReads);
    }

    [TestMethod]
    public void CloseBatch_DecidesTheFreedSlotsOnceAndFeedsThemOneAtATime()
    {
        var shown = ShowCards(1, 5);
        Pump();
        ShowCards(6, 10);
        _screen.ScreenReads = 0;

        _queue.CloseBatch(shown);

        // Five slots went at once, so the batch is decided about once. But only one card arrives: a burst that
        // filled every free slot in the same frame was due again on the same tick several seconds later, which
        // is what arrived as four windows vanishing and the stack re-sliding inside twelve milliseconds.
        Assert.AreEqual(1, _screen.ScreenReads);
        Assert.HasCount(6, _screen.Shown);
        Assert.AreEqual("card 6", _screen.Shown[5].Request.Message);

        // The rest of the room is filled off the clock, one card per pass, and stops at the cap.
        _queue.Feed();
        _queue.Feed();
        _queue.Feed();
        Assert.HasCount(9, _screen.Shown);
        _queue.Feed();
        Assert.HasCount(10, _screen.Shown);
        _queue.Feed();
        Assert.HasCount(10, _screen.Shown);
        foreach (var item in shown) Assert.AreEqual(NotificationResult.Success, ResultOf(item));
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
        Pump();
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
        CollectionAssert.Contains(_screen.Hidden, card);
    }

    [TestMethod]
    public void Dismiss_WaitsForTheGateTheHostIsHolding()
    {
        var card = ShowCard("withdrawn while the host is busy");

        // The deterministic half of the concurrency claim: hold the lock a submission takes, then dismiss from
        // another thread. A dismissal that walked straight into the lists would finish the item through the lock
        // the host is holding, which is the fault the shared gate exists to stop.
        var started = new ManualResetEventSlim(false);
        var dismissed = new ManualResetEventSlim(false);
        var worker = new Thread(() =>
        {
            started.Set();
            card.Dismiss();
            dismissed.Set();
        });

        Monitor.Enter(_gate);
        try
        {
            worker.Start();
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(2)), "the worker never reached the dismissal");
            Assert.IsFalse(dismissed.Wait(TimeSpan.FromMilliseconds(200)),
                "the handle dismissed the notification without waiting for the gate");
        }
        finally
        {
            Monitor.Exit(_gate);
        }

        worker.Join(TimeSpan.FromSeconds(5));
        Assert.AreEqual(NotificationResult.Success, ResultOf(card));
    }

    [TestMethod]
    public void DismissFromSeveralThreads_LeavesTheQueueConsistent()
    {
        // The handle is the one entry point a plugin reaches on its own thread with no host frame around it, so
        // it has to take the gate the way Submit does. Driven hard here because the fault was a torn list, not a
        // wrong decision: five visible plus five queued for this plugin is all the room there is, and every card
        // past that is meant to come back QueueFull rather than corrupt its neighbours.
        const int threads = 4;
        var items = ShowCards(1, 40);
        var failures = new List<string>();

        var workers = Enumerable.Range(0, threads).Select(worker => new Thread(() =>
        {
            try
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    for (var i = worker; i < items.Length; i += threads) items[i].Dismiss();
                }
            }
            catch (Exception ex)
            {
                lock (failures) failures.Add(ex.Message);
            }
        })).ToArray();
        foreach (var worker in workers) worker.Start();
        foreach (var worker in workers) worker.Join();

        Assert.HasCount(0, failures);
        foreach (var item in items)
            Assert.IsTrue(item.Completion.IsCompleted, $"nothing ever decided the end of \"{item.Request.Message}\"");
        Assert.HasCount(0, _screen.Visible);
    }

    [TestMethod]
    public void WithdrawingAnAlreadyReplacedNotice_LeavesTheNewOneAlone()
    {
        var first = ShowNotice("withdrawn after the fact");
        var replacement = ShowNotice("the line now showing");

        first.Dismiss();

        Assert.AreEqual(NotificationFailure.Replaced, ResultOf(first).Failure);
        Assert.IsTrue(IsOutstanding(replacement));
        Assert.IsFalse(_screen.Hidden.Contains(replacement));
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

    /// <summary>Runs the promotions the service's clock would run, until the screen is full or nothing is left
    /// waiting. Admitting a burst no longer means presenting it -- arrivals are paced a card per tick -- so a test
    /// that means "five cards on screen" has to say so, rather than assume the queue filled the slots on its own.</summary>
    private void Pump()
    {
        for (var turn = 0; turn < 16 && _queue.HasWaiting; turn++) _queue.Feed();
    }

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
        // Recorded under a lock: the concurrency case submits and dismisses from several threads, and a fake
        // that races on its own recorder would report a fault the production queue never had.
        private readonly object _recorded = new();
        public List<NotificationItem> Shown { get; } = [];
        public List<NotificationItem> Hidden { get; } = [];
        public List<string> Warnings { get; } = [];
        public bool Fullscreen { get; set; }

        /// <summary>How many times the queue asked what the screen is doing, which is the only way a test can
        /// say "once for the whole refill" rather than take it on trust.</summary>
        public int ScreenReads { get; set; }

        public bool ReadScreen()
        {
            ScreenReads++;
            return Fullscreen;
        }

        public void Show(NotificationItem item)
        {
            lock (_recorded) Shown.Add(item);
        }

        public void Hide(NotificationItem item)
        {
            lock (_recorded) Hidden.Add(item);
        }

        public void Warn(string message)
        {
            lock (_recorded) Warnings.Add(message);
        }

        /// <summary>What a real screen would still be showing: presented and not taken down again.</summary>
        public List<NotificationItem> Visible
        {
            get
            {
                lock (_recorded) return Shown.Where(item => !Hidden.Contains(item)).ToList();
            }
        }
    }
}
