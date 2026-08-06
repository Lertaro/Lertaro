using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// Time is passed in rather than read, so these pin the decision with no sleeping and nothing
// timing-dependent. What they protect: trimming the working set frees no committed memory at all, it
// only evicts pages the next summon must fault back in -- measured at ~17MB and 70% of a summon's time
// on every single summon, and at 4.7 seconds with 112,876 faults once, during an index rebuild.
[TestClass]
public sealed class IdleWorkingSetTrimGateTests
{
    private const long IdleMs = 15_000;

    private static IdleWorkingSetTrimGate Gate() => new(IdleMs);

    [TestMethod]
    public void AWindowJustHidden_IsNotTrimmedYet()
    {
        var gate = Gate();
        gate.WindowHidden(0);

        Assert.IsFalse(gate.ShouldTrim(100));
        Assert.IsFalse(gate.ShouldTrim(IdleMs - 1));
    }

    [TestMethod]
    public void AWindowLeftAlone_IsEventuallyTrimmed()
    {
        // Giving the memory back still has to happen -- this defers it, it does not abandon it.
        var gate = Gate();
        gate.WindowHidden(0);

        Assert.IsTrue(gate.ShouldTrim(IdleMs));
    }

    [TestMethod]
    public void ABurstOfSummons_NeverPaysForATrim()
    {
        // The real usage the trace captured: show and hide 300ms apart, over and over. Every one of
        // those used to empty the working set and re-fault ~17MB.
        var gate = Gate();
        for (var t = 0L; t < 10_000; t += 300)
        {
            gate.WindowHidden(t);
            gate.WindowShowing();
            Assert.IsFalse(gate.ShouldTrim(t + 150));
        }

        Assert.IsFalse(gate.ShouldTrim(60_000), "the last thing that happened was a summon, not a hide");
    }

    [TestMethod]
    public void ASummonCancelsAPendingTrim()
    {
        // Trimming moments before a summon is strictly worse than never trimming: the pages are about
        // to be needed.
        var gate = Gate();
        gate.WindowHidden(0);
        gate.WindowShowing();

        Assert.IsFalse(gate.ShouldTrim(IdleMs));
        Assert.IsFalse(gate.ShouldTrim(600_000));
    }

    [TestMethod]
    public void TheIdleWindowRunsFromTheMostRecentHide()
    {
        var gate = Gate();
        gate.WindowHidden(0);
        gate.WindowShowing();
        gate.WindowHidden(10_000);

        Assert.IsFalse(gate.ShouldTrim(20_000), "only 10s since the hide that matters");
        Assert.IsTrue(gate.ShouldTrim(25_000));
    }

    [TestMethod]
    public void TrimmingHappensOncePerHide()
    {
        var gate = Gate();
        gate.WindowHidden(0);

        Assert.IsTrue(gate.ShouldTrim(IdleMs));
        Assert.IsFalse(gate.ShouldTrim(IdleMs + 10_000), "nothing has happened since, so there is nothing to give back");
        Assert.IsFalse(gate.ShouldTrim(999_999));
    }

    [TestMethod]
    public void AProcessThatHasNeverShownTheWindow_NeverTrims()
    {
        var gate = Gate();

        Assert.IsFalse(gate.ShouldTrim(600_000));
    }

    [TestMethod]
    public void HidingAgainAfterATrim_ArmsItAgain()
    {
        var gate = Gate();
        gate.WindowHidden(0);
        Assert.IsTrue(gate.ShouldTrim(IdleMs));

        gate.WindowShowing();
        gate.WindowHidden(100_000);

        Assert.IsTrue(gate.ShouldTrim(100_000 + IdleMs));
    }
}
