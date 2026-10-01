using System.IO;
using Lertaro.Plugins.Calendar.Reminders;

namespace Lertaro.Plugins.Calendar.Tests.Reminders;

[TestClass]
public sealed class ReminderEngineTests
{
    private static readonly DateTime Morning = new(2026, 9, 30, 9, 0, 0);

    [TestMethod]
    [DataRow(1)]
    [DataRow(30)]
    [DataRow(600)]
    public void Decide_WaitsForAnythingInTheFuture(int minutesAhead) => Assert.AreEqual(ReminderOutcome.Wait, ReminderEngine.Decide(
            Morning.AddMinutes(minutesAhead), null, Morning, ReminderEngine.Grace, ReminderEngine.LateAfter));

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(19)]
    public void Decide_FiresAnythingInsideTheLateWindow(int minutesPast) => Assert.AreEqual(ReminderOutcome.Fire, ReminderEngine.Decide(
            Morning, null, Morning.AddMinutes(minutesPast), ReminderEngine.Grace, ReminderEngine.LateAfter));

    [TestMethod]
    public void Decide_FiresLateOnceItIsPastTheGraceOfTheMoment() => Assert.AreEqual(ReminderOutcome.FireLate, ReminderEngine.Decide(
            Morning, null, Morning.AddMinutes(21), ReminderEngine.Grace, ReminderEngine.LateAfter));

    [TestMethod]
    public void Decide_BeyondTheGraceWindowItExpiresInsteadOfAnnouncing()
    {
        Assert.AreEqual(ReminderOutcome.Expire, ReminderEngine.Decide(
            Morning, null, Morning.Add(ReminderEngine.Grace).AddMinutes(1), ReminderEngine.Grace, ReminderEngine.LateAfter));

        // Exactly at the boundary is still worth telling the user about.
        Assert.AreEqual(ReminderOutcome.FireLate, ReminderEngine.Decide(
            Morning, null, Morning.Add(ReminderEngine.Grace), ReminderEngine.Grace, ReminderEngine.LateAfter));
    }

    [TestMethod]
    public void Decide_AlreadyDeliveredIsPrunedNotRepeated() => Assert.AreEqual(ReminderOutcome.Expire, ReminderEngine.Decide(
            Morning, Morning.AddSeconds(3), Morning.AddMinutes(5), ReminderEngine.Grace, ReminderEngine.LateAfter));

    [TestMethod]
    public void Reconcile_DeliversEveryReminderDueOnTheSameTick()
    {
        using var harness = new Harness();

        for (var i = 0; i < 4; i++)
            harness.Store.Add(Morning, "item " + i);

        // Four reminders for the same minute go out on one pass: the host stacks five cards with five more
        // queued behind them, so there is no reason to spread a single moment's reminders over a minute each.
        Assert.AreEqual(4, harness.Engine.Reconcile(Morning));
        Assert.AreEqual(0, harness.Engine.Reconcile(Morning.AddMinutes(1)), "and none of them is left to fire again");

        CollectionAssert.AreEqual(
            new[] { "item 0", "item 1", "item 2", "item 3" },
            harness.Presented.Select(p => p.Reminder.Text).OrderBy(t => t, StringComparer.Ordinal).ToList());
        Assert.AreEqual(4, harness.Presented.Count(p => !p.Late), "each went out inside the late window");
        Assert.AreEqual(0, harness.Store.Snapshot().Count, "and none of them is left to fire again");
    }

    [TestMethod]
    public void Reconcile_AfterTheAppWasClosed_DeliversTheBacklogOldestFirstAndDropsWhatIsTooOld()
    {
        using var harness = new Harness();
        harness.Store.Add(Morning.AddDays(-1), "yesterday morning");
        harness.Store.Add(Morning.AddDays(-2), "two days out");
        harness.Store.Add(Morning.AddHours(-3), "three hours ago");
        harness.Store.Add(Morning.AddHours(-30), "older than the grace window");

        // First tick after startup is the catch-up, so there is no separate cold-start path to get wrong.
        // Everything still inside the 24 hour grace is announced on that pass, oldest first, and the two
        // beyond it are dropped silently rather than announced a day late.
        Assert.AreEqual(2, harness.Engine.Reconcile(Morning));
        Assert.AreEqual("yesterday morning", harness.Presented[0].Reminder.Text);
        Assert.AreEqual("three hours ago", harness.Presented[1].Reminder.Text);
        Assert.IsTrue(harness.Presented.All(p => p.Late), "reminders that went out hours late say so");
        Assert.AreEqual(2, harness.Store.Snapshot().Count, "the two past the grace window are gone, and the two " +
            "just delivered stay only until the next tick prunes them");

        Assert.AreEqual(0, harness.Engine.Reconcile(Morning.AddMinutes(1)), "a stamped reminder never fires twice");
        Assert.AreEqual(0, harness.Store.Snapshot().Count);
    }

    [TestMethod]
    public void Reconcile_LeavesAFutureReminderAlone()
    {
        using var harness = new Harness();
        var added = harness.Store.Add(Morning.AddHours(2), "later today");

        Assert.AreEqual(0, harness.Engine.Reconcile(Morning));
        Assert.AreEqual(0, harness.Presented.Count);
        Assert.AreEqual(1, harness.Store.Snapshot().Count);
        Assert.AreEqual(added.Id, harness.Store.Snapshot()[0].Id);
    }

    [TestMethod]
    public void Reconcile_PersistsDeliveryBeforeHandingTheReminderOver()
    {
        using var harness = new Harness();
        harness.Store.Add(Morning, "one shot");

        // The notifier throwing stands in for a host that refuses the notification. Reconcile itself does
        // not swallow it, the tick loop does, so what has to hold either way is that the record was
        // already written: delivery is at-most-once, and a lost reminder beats one that returns on every
        // start for the rest of the day. The real notifier cannot throw, which is why nothing here catches.
        Assert.ThrowsExactly<InvalidOperationException>(() => harness.Engine.Reconcile(Morning));

        Assert.AreEqual(0, harness.Engine.Reconcile(Morning.AddMinutes(1)));
        Assert.AreEqual(0, harness.Presented.Count);
        Assert.AreEqual(0, harness.Store.Snapshot().Count, "and it is pruned rather than left pending");
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("lertaro-calendar-").FullName;

        public Harness()
        {
            Store = new ReminderStore(_root);
            Engine = new ReminderEngine(Store, Present);
        }

        internal ReminderStore Store { get; }
        internal ReminderEngine Engine { get; }
        internal List<(CalendarReminder Reminder, bool Late)> Presented { get; } = new();

        private void Present(CalendarReminder reminder, bool late)
        {
            if (reminder.Text == "one shot") throw new InvalidOperationException("the host refused the notification");
            Presented.Add((reminder, late));
        }

        public void Dispose()
        {
            Engine.Dispose();
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }
}
