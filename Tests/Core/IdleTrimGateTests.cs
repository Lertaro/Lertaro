namespace Lertaro.Core.Tests;

// Time is passed in rather than read, so these pin the decision itself with no sleeping and no timer.
[TestClass]
public sealed class IdleTrimGateTests
{
    private const long IdleMs = 3000;

    private static IdleTrimGate At(long start = 0) => new(IdleMs, start);

    [TestMethod]
    public void ASearchStillRunning_IsNotIdle_HoweverLongAgoItStarted()
    {
        // The bug. A whole-drive query blocks for longer than the idle window, so the trimmer used to
        // fire into the middle of it while everything it wanted was still live.
        var gate = At();
        gate.SearchStarted(0);

        Assert.IsFalse(gate.ShouldTrim(10_000), "a search that is still running is not idle");
    }

    [TestMethod]
    public void AfterALongSearchFinishes_ItStillTrims()
    {
        // And the consequence of the above: the mid-search tick must not have eaten the one chance.
        var gate = At();
        gate.SearchStarted(0);
        gate.ShouldTrim(10_000); // the tick that used to consume the arming
        gate.SearchFinished(10_000);

        Assert.IsFalse(gate.ShouldTrim(11_000), "the idle window runs from when the search ended");
        Assert.IsTrue(gate.ShouldTrim(14_000));
    }

    [TestMethod]
    public void AShortSearch_TrimsOnceItHasBeenQuietLongEnough()
    {
        // This path always worked, which is why the failure looked size-dependent rather than timing-dependent.
        var gate = At();
        gate.SearchStarted(0);
        gate.SearchFinished(500);

        Assert.IsFalse(gate.ShouldTrim(3000));
        Assert.IsTrue(gate.ShouldTrim(3600));
    }

    [TestMethod]
    public void TrimmingHappensOncePerBurstOfActivity()
    {
        var gate = At();
        gate.SearchStarted(0);
        gate.SearchFinished(100);

        Assert.IsTrue(gate.ShouldTrim(4000));
        Assert.IsFalse(gate.ShouldTrim(9000), "nothing has happened since, so there is nothing to reclaim");
        Assert.IsFalse(gate.ShouldTrim(90_000));
    }

    [TestMethod]
    public void NewActivity_ArmsItAgain()
    {
        var gate = At();
        gate.SearchStarted(0);
        gate.SearchFinished(100);
        Assert.IsTrue(gate.ShouldTrim(4000));

        gate.SearchStarted(5000);
        gate.SearchFinished(5100);

        Assert.IsTrue(gate.ShouldTrim(9000));
    }

    [TestMethod]
    public void AnIdleProcessThatHasDoneNothing_NeverTrims()
    {
        // Freshly started and never searched: stopping the world would buy nothing.
        var gate = At();

        Assert.IsFalse(gate.ShouldTrim(60_000));
    }

    [TestMethod]
    public void OverlappingSearches_StayInFlightUntilTheLastOneEnds()
    {
        // The quick window can supersede a query without the outgoing one having returned yet.
        var gate = At();
        gate.SearchStarted(0);
        gate.SearchStarted(200);
        gate.SearchFinished(400);

        Assert.IsFalse(gate.ShouldTrim(9000), "one of the two is still running");

        gate.SearchFinished(9000);
        Assert.IsTrue(gate.ShouldTrim(13_000));
    }

    [TestMethod]
    public void ASearchStartingDuringTheIdleWindow_DefersTheTrim()
    {
        var gate = At();
        gate.SearchStarted(0);
        gate.SearchFinished(100);

        gate.SearchStarted(3000);
        Assert.IsFalse(gate.ShouldTrim(4000), "the trim must not land on top of the new search");
    }
}
