using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class InlineSmallResultRenderDelayTests
{
    [TestMethod]
    public void ShouldDelay_FewerThanFirstRenderThreshold_TrueBeforeDeadline() =>
        Assert.IsTrue(InlineSmallResultRenderDelay.ShouldDelay(ProgressiveRenderPlan.MinimumFirstRender - 1, InlineSmallResultRenderDelay.SettleDelayMs - 1));

    [TestMethod]
    public void ShouldDelay_AtFirstRenderThreshold_False() =>
        Assert.IsFalse(InlineSmallResultRenderDelay.ShouldDelay(ProgressiveRenderPlan.MinimumFirstRender, 0));

    [TestMethod]
    public void ShouldDelay_DeadlineReached_False() =>
        Assert.IsFalse(InlineSmallResultRenderDelay.ShouldDelay(0, InlineSmallResultRenderDelay.SettleDelayMs));
}
