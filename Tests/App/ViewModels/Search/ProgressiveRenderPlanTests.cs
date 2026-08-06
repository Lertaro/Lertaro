using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class ProgressiveRenderPlanTests
{
    [TestMethod]
    public void BelowTheFirstRenderThreshold_PaintsNothing()
    {
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(0, plan.NextRenderSize(ProgressiveRenderPlan.MinimumFirstRender - 1, 0));
        Assert.AreEqual(0, plan.Rendered);
    }

    [TestMethod]
    public void AtTheFirstRenderThreshold_PaintsEverythingReceived()
    {
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(ProgressiveRenderPlan.MinimumFirstRender, plan.NextRenderSize(ProgressiveRenderPlan.MinimumFirstRender, 0));
    }

    [TestMethod]
    public void TheFirstPaintIsCappedEvenWhenEverythingHasAlreadyArrived()
    {
        // A search resolving faster than the first tick must not turn that tick into a paint of the
        // entire result set -- the first paint is the one that has to be immediate.
        var plan = new ProgressiveRenderPlan();

        Assert.AreEqual(ProgressiveRenderPlan.FirstRenderCap, plan.NextRenderSize(5_000_000, 0));
    }

    [TestMethod]
    public void NothingNew_PaintsNothing()
    {
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(500, 0);

        Assert.AreEqual(0, plan.NextRenderSize(500, 10_000));
    }

    [TestMethod]
    public void ACheapPaint_LetsTheNextTickThroughImmediately()
    {
        // The behaviour the displayed count depends on. Once a paint only touches the rows that
        // actually changed it costs almost nothing, and there is no reason to make the list -- and so
        // the number the user is reading -- wait for an arbitrary growth factor before moving again.
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(2_000, 0);
        plan.PaintCompleted(0);

        Assert.AreEqual(2_050, plan.NextRenderSize(2_050, 0));
        plan.PaintCompleted(1);
        Assert.AreEqual(2_100, plan.NextRenderSize(2_100, 150));
    }

    [TestMethod]
    public void AnExpensivePaint_HoldsTheNextOneOff()
    {
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(2_000, 0);
        plan.PaintCompleted(200);

        Assert.AreEqual(0, plan.NextRenderSize(500_000, 200 * ProgressiveRenderPlan.IdleMultiplier - 1));
        Assert.AreEqual(500_000, plan.NextRenderSize(500_000, 200 * ProgressiveRenderPlan.IdleMultiplier));
    }

    [TestMethod]
    public void TheBudgetScalesWithHowExpensiveTheLastPaintWas()
    {
        // A share of wall-clock, not a fixed delay: a paint that took ten times as long waits ten times
        // as long, which is what holds the UI thread's share roughly constant however the cost moves.
        var cheap = new ProgressiveRenderPlan();
        cheap.NextRenderSize(2_000, 0);
        cheap.PaintCompleted(10);
        var dear = new ProgressiveRenderPlan();
        dear.NextRenderSize(2_000, 0);
        dear.PaintCompleted(100);

        var idle = 10 * ProgressiveRenderPlan.IdleMultiplier;
        Assert.AreEqual(3_000, cheap.NextRenderSize(3_000, idle));
        Assert.AreEqual(0, dear.NextRenderSize(3_000, idle));
    }

    [TestMethod]
    public void EachPaintShowsEverythingReceivedByThen()
    {
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(3_000, 0);
        plan.PaintCompleted(0);

        Assert.AreEqual(50_000, plan.NextRenderSize(50_000, 0));
    }

    [TestMethod]
    public void ASkippedTickLeavesThePlanExactlyWhereItWas()
    {
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(50, 0);
        plan.PaintCompleted(100);

        plan.NextRenderSize(60, 0);
        plan.NextRenderSize(70, 0);

        Assert.AreEqual(50, plan.Rendered);
        Assert.AreEqual(100, plan.NextRenderSize(100, 100 * ProgressiveRenderPlan.IdleMultiplier));
    }

    [TestMethod]
    public void AHugeBacklogDoesNotOverflowTheBudgetComparison()
    {
        var plan = new ProgressiveRenderPlan();
        plan.NextRenderSize(int.MaxValue, 0);
        plan.PaintCompleted(long.MaxValue / 4);

        Assert.AreEqual(0, plan.NextRenderSize(int.MaxValue, 1));
    }
}
