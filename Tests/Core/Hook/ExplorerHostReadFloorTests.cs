using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

// Clock-free by construction: every method takes `nowTicks`, so these are arithmetic rather than sleeps,
// which is the reason the rule is its own class instead of three conditions inside
// ExplorerActivePathPoller.PollCore.
[TestClass]
public sealed class ExplorerHostReadFloorTests
{
    private static readonly IntPtr Window = new(0x1000);
    private static readonly IntPtr Other = new(0x2000);

    // The symptom this half exists for: XYplorer raising name-change/focus events because the pointer moved
    // through its list is not a request for its path, and reading it there is what cancels the info tip the
    // events were about.
    [TestMethod]
    public void AllowsRead_PointerChatterAloneAsksForNothing()
    {
        var floor = new ExplorerHostReadFloor();

        Assert.IsFalse(floor.AllowsRead(Window, nowTicks: 0));
    }

    // A window switch reads at once, interval or not: the card needs the new window's folder the moment it
    // opens, and a switch cannot have been manufactured by hovering.
    [TestMethod]
    public void AllowsRead_ForegroundChangeReadsInsideTheInterval()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);
        floor.RequestForegroundRead();

        Assert.IsTrue(floor.AllowsRead(Window, 1001));
        Assert.IsFalse(floor.AllowsRead(Window, 1002));
    }

    [TestMethod]
    public void AllowsRead_RequestIsConsumedByTheReadItWins()
    {
        var floor = new ExplorerHostReadFloor();
        floor.RequestRead();

        Assert.IsTrue(floor.AllowsRead(Window, nowTicks: 0));
        Assert.IsFalse(floor.AllowsRead(Window, nowTicks: 1));
    }

    // A request held back by the interval must not be spent on the attempt: a summon keystroke arriving 50ms
    // after a read would otherwise be dropped silently, and with no further events the card's folder would
    // stay wrong until the next window switch.
    [TestMethod]
    public void AllowsRead_RequestHeldBackByTheIntervalStaysPending()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);
        floor.RequestRead();

        Assert.IsFalse(floor.AllowsRead(Window, 1500));
        Assert.IsTrue(floor.AllowsRead(Window, 1000 + ExplorerHostReadFloor.MinHostReadMs));
    }

    // Steady demand while the card is up -- its scope and dock still have to follow the host -- but hovering
    // must not convert that into one read per burst, so the interval applies.
    [TestMethod]
    public void AllowsRead_CardOnScreenReadsOncePerInterval()
    {
        var floor = new ExplorerHostReadFloor { CardOnScreen = true };

        Assert.IsTrue(floor.AllowsRead(Window, nowTicks: 0));
        floor.NoteRead(Window, nowTicks: 0);

        Assert.IsFalse(floor.AllowsRead(Window, 1000));
        Assert.IsTrue(floor.AllowsRead(Window, ExplorerHostReadFloor.MinHostReadMs));
    }

    [TestMethod]
    public void ClearCardOnScreen_DropsSteadyAndEveryPendingDemand()
    {
        var floor = new ExplorerHostReadFloor { CardOnScreen = true };
        floor.RequestRead();
        floor.RequestForegroundRead();

        floor.ClearCardOnScreen();

        Assert.IsFalse(floor.AllowsRead(Window, nowTicks: 0));
        Assert.IsFalse(floor.AllowsRead(Window, nowTicks: 1));
    }

    [TestMethod]
    public void Blocks_SameWindowInsideTheInterval_IsHeldBack()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);

        Assert.IsTrue(floor.Blocks(Window, 1000 + ExplorerHostReadFloor.MinHostReadMs - 1));
        Assert.IsFalse(floor.Blocks(Window, 1000 + ExplorerHostReadFloor.MinHostReadMs));
    }

    // The other half of the same point: only a read re-notes, so the cadence stays "one read per interval"
    // rather than "one read per interval of quiet", which pointer movement could postpone forever.
    [TestMethod]
    public void Blocks_BlockedAttemptsDoNotPushTheIntervalOut()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);
        Assert.IsTrue(floor.Blocks(Window, 2500));

        Assert.IsFalse(floor.Blocks(Window, 1000 + ExplorerHostReadFloor.MinHostReadMs));
    }

    // A different window is never held back by this one: alt-tabbing between two hosts must not cost a wait.
    [TestMethod]
    public void Blocks_ADifferentWindow_IsNeverHeldBackByThisOne()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);

        Assert.IsFalse(floor.Blocks(Other, 1001));
    }

    // Nothing tracked yet: an unknown window has to be readable, or a host that was never claimed stays
    // unclaimed (the reason UnclaimedDialogRetryLimit exists).
    [TestMethod]
    public void Blocks_NoWindowToTrack_IsNotAnObstacle()
    {
        var floor = new ExplorerHostReadFloor();
        floor.NoteRead(Window, nowTicks: 1000);

        Assert.IsFalse(floor.Blocks(IntPtr.Zero, 1001));
    }
}
