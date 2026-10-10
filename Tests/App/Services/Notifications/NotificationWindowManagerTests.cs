using Lertaro.App.Services.Notifications;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Services.Notifications;

[TestClass]
[DoNotParallelize]
public sealed class NotificationWindowManagerTests
{
    [StaTestMethod]
    public void ExternalClose_ReleasesTheRunnerAndCompletesItsQueueItem()
    {
        NotificationWindowManager? windows = null;
        NotificationQueue? queue = null;
        var closed = new List<NotificationItem>();
        queue = new NotificationQueue(() => false, item => windows!.Present(item),
            item => windows!.TakeDown(item), _ => { }, new object());
        windows = new NotificationWindowManager(item =>
        {
            closed.Add(item);
            queue.NotifyClosed(item);
        }, items => queue.CloseBatch(items));
        try
        {
            var first = queue.Submit(Request("first"), "plugin", "Plugin");
            var waiting = queue.Submit(Request("waiting"), "plugin", "Plugin");

            windows.Countdown().Single().Window.Close();

            Assert.IsTrue(first.Completion.IsCompleted);
            Assert.AreEqual(NotificationResult.Success, first.Completion.Result);
            Assert.ContainsSingle(closed);
            Assert.AreEqual(0, windows.Count);
            queue.Feed();
            Assert.AreSame(waiting, windows.Countdown().Single().Item);
            windows.HideForSession(true);
            windows.HideForSession(false);
        }
        finally
        {
            queue.Shutdown();
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void ManagedBatchClose_ReportsOnlyTheBatchAndNoIndividualCloses()
    {
        var singles = new List<NotificationItem>();
        var batches = new List<IReadOnlyList<NotificationItem>>();
        var windows = new NotificationWindowManager(singles.Add, batches.Add);
        try
        {
            windows.Present(Item("first"));
            windows.Present(Item("second"));

            windows.CloseBatch(windows.Countdown().ToArray());

            Assert.AreEqual(0, windows.Count);
            Assert.IsEmpty(singles);
            Assert.HasCount(2, Assert.ContainsSingle(batches));
        }
        finally
        {
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void SameSlotReplacement_KeepsTheWindowAndSurvivorsSlide()
    {
        var gone = new List<NotificationItem>();
        var windows = new NotificationWindowManager(gone.Add, _ => { });
        try
        {
            var first = Item("first");
            windows.Present(first);
            windows.Present(Item("later"));
            var original = windows.Countdown().First();
            var survivor = windows.Countdown().Last();
            var slide = survivor.Moving;
            var top = survivor.Window.Top;
            original.RemainingMs = 1;
            first.Complete(NotificationResult.Failed(NotificationFailure.Replaced));
            var replacement = Item("updated");
            replacement.Sequence = first.Sequence;

            windows.Present(replacement);

            Assert.AreEqual(2, windows.Count);
            Assert.AreSame(original, windows.Countdown().First(r => r.Item == replacement));
            Assert.AreEqual(8000, original.RemainingMs);
            Assert.AreEqual(slide, survivor.Moving);
            Assert.AreEqual(top, survivor.Window.Top, 0.1);
            Assert.IsEmpty(gone);
            original.Window.Close();
            Assert.AreSame(replacement, Assert.ContainsSingle(gone));
        }
        finally
        {
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void CancelledReplacement_BeforePresentation_ClosesTheOriginalSlot()
    {
        var pending = new List<NotificationItem>();
        var windows = new NotificationWindowManager(_ => { }, _ => { });
        var queue = new NotificationQueue(() => false, pending.Add, windows.TakeDown, _ => { }, new object());
        try
        {
            NotificationItem Submit(string text) => queue.Submit(
                new NotificationRequest { Title = text, Id = "same slot" }, "plugin", "Plugin");
            var first = Submit("first");
            windows.Present(first);
            Submit("middle");
            var replacement = Submit("last");

            replacement.Dismiss();
            foreach (var item in pending) windows.Present(item);

            Assert.AreEqual(0, windows.Count);
            Assert.AreEqual(NotificationResult.Success, replacement.Completion.Result);
        }
        finally
        {
            queue.Shutdown();
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void LockBeforeFirstPresentation_KeepsNewWindowsHiddenUntilUnlock()
    {
        var windows = new NotificationWindowManager(_ => { }, _ => { });
        try
        {
            windows.HideForSession(true);
            windows.Present(Item("arrived while locked"));
            var runner = windows.Countdown().Single();

            Assert.IsFalse(runner.Window.IsVisible);
            Assert.AreEqual(8000, runner.RemainingMs);
            windows.HideForSession(false);
            Assert.IsTrue(runner.Window.IsVisible);
            Assert.AreEqual(8000, runner.RemainingMs);
        }
        finally
        {
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void StackCapacity_DefersOverflowAndRestoresUnspentTime()
    {
        var area = new System.Windows.Rect(-100, -200, 800, 1040);
        NotificationWindowManager? windows = null;
        var queue = new NotificationQueue(() => false, item => windows!.Present(item),
            item => windows!.TakeDown(item), _ => { }, new object());
        windows = new NotificationWindowManager(item =>
        {
            windows!.Restack(false);
            queue.NotifyClosed(item);
        }, queue.CloseBatch, queue.Defer, queue.SetCardCapacity, () => area);
        try
        {
            var items = Enumerable.Range(0, 5).Select(i => queue.Submit(new NotificationRequest
            {
                Title = $"card {i}", Message = string.Join("\n", Enumerable.Repeat("long body", 40))
            }, "plugin", "Plugin")).ToArray();
            for (var i = 0; i < 10; i++) queue.Feed();

            Assert.AreEqual(3, windows.Count);
            Assert.IsTrue(queue.HasWaiting);
            Assert.IsTrue(items.All(item => !item.Completion.IsCompleted));
            foreach (var runner in windows.Countdown())
            {
                Assert.AreEqual(260, runner.Window.ActualHeight, 0.1);
                Assert.IsTrue(runner.Window.Top >= area.Top + NotificationWindowManager.EdgeMarginDip);
                runner.RemainingMs = 1234;
            }

            area = new System.Windows.Rect(-100, -200, 800, 550);
            windows.Reanchor();
            Assert.AreEqual(1, windows.Count);
            for (var i = 0; i < 10; i++) queue.Feed();
            Assert.AreEqual(1, windows.Count);

            area = new System.Windows.Rect(-100, -200, 800, 1800);
            windows.Reanchor();
            for (var i = 0; i < 10; i++) queue.Feed();
            Assert.AreEqual(5, windows.Count);
            Assert.AreEqual(1234, windows.Countdown().Single(r => r.Item == items[1]).RemainingMs);
            Assert.IsTrue(items.All(item => !item.Completion.IsCompleted));
        }
        finally
        {
            queue.Shutdown();
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void TallerReplacement_DefersOverflowWithoutCompletingIt()
    {
        NotificationWindowManager? windows = null;
        var queue = new NotificationQueue(() => false, item => windows!.Present(item),
            item => windows!.TakeDown(item), _ => { }, new object());
        windows = new NotificationWindowManager(queue.NotifyClosed, queue.CloseBatch,
            queue.Defer, queue.SetCardCapacity, () => new System.Windows.Rect(0, 0, 800, 550));
        try
        {
            queue.Submit(new NotificationRequest { Title = "short", Id = "progress" }, "plugin", "Plugin");
            var longBody = string.Join("\n", Enumerable.Repeat("long body", 40));
            var later = queue.Submit(new NotificationRequest { Message = longBody }, "plugin", "Plugin");
            queue.Feed();
            Assert.AreEqual(2, windows.Count);
            var original = windows.Countdown().First().Window;
            windows.Countdown().Last().RemainingMs = 1234;

            var replacement = queue.Submit(new NotificationRequest { Message = longBody, Id = "progress" },
                "plugin", "Plugin");

            Assert.AreEqual(1, windows.Count);
            Assert.AreSame(original, windows.Countdown().Single().Window);
            Assert.AreSame(replacement, windows.Countdown().Single().Item);
            Assert.IsFalse(later.Completion.IsCompleted);
            Assert.AreEqual(1234, later.RemainingMs);
            Assert.IsTrue(queue.HasWaiting);
        }
        finally
        {
            queue.Shutdown();
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void ClosingLastCard_RefreshesCapacityBeforeRefilling()
    {
        NotificationWindowManager? windows = null;
        var queue = new NotificationQueue(() => false, item => windows!.Present(item),
            item => windows!.TakeDown(item), _ => { }, new object());
        windows = new NotificationWindowManager(item =>
        {
            windows!.Restack(false);
            queue.NotifyClosed(item);
        }, queue.CloseBatch, queue.Defer, queue.SetCardCapacity,
            () => new System.Windows.Rect(0, 0, 800, 300));
        try
        {
            NotificationItem Submit() => queue.Submit(new NotificationRequest
            {
                Message = string.Join("\n", Enumerable.Repeat("long body", 40))
            }, "plugin", "Plugin");
            var first = Submit();
            var next = Submit();
            queue.Feed();
            Assert.AreEqual(1, windows.Count);
            Assert.IsFalse(next.Completion.IsCompleted);
            windows.Countdown().Single().Window.Close();
            Assert.AreEqual(NotificationResult.Success, first.Completion.Result);
            Assert.AreEqual(0, windows.Count);
            queue.Feed();
            Assert.AreSame(next, windows.Countdown().Single().Item);
        }
        finally
        {
            queue.Shutdown();
            windows.CloseEverything();
        }
    }

    [StaTestMethod]
    public void DragStart_CancelsSlideBeforeNestedRestacking()
    {
        var windows = new NotificationWindowManager(_ => { }, _ => { });
        try
        {
            windows.Present(Item("drag me"));
            var runner = windows.Countdown().Single();
            var card = (Lertaro.App.Views.Notifications.NotificationCardWindow)runner.Window;
            Assert.IsNotNull(runner.Moving);
            card.BeginUserDrag();
            Assert.IsNull(runner.Moving);
            card.Top = 100;
            card.Left = 200;
            windows.Restack(true);
            Assert.AreEqual(100, card.Top);
            Assert.AreEqual(200, card.Left);
            card.EndUserDrag();
            Assert.AreEqual(100, card.Top);
            Assert.IsNull(runner.Moving);
        }
        finally
        {
            windows.CloseEverything();
        }
    }

    private static NotificationRequest Request(string text) => new() { Title = text, Message = text };

    private static NotificationItem Item(string text) =>
        new(Request(text), "plugin", "Plugin", NotificationPosition.CardStack, 8, null);
}
