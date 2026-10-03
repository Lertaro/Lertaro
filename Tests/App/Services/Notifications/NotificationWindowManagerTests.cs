using Lertaro.App.Services.Notifications;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Services.Notifications;

[TestClass]
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

    private static NotificationRequest Request(string text) => new() { Title = text, Message = text };

    private static NotificationItem Item(string text) =>
        new(Request(text), "plugin", "Plugin", NotificationPosition.CardStack, 8, null);
}
