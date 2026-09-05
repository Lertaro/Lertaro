using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class MarqueeBehaviorTests
{
    [TestMethod]
    public void CalculateMarqueeSpeed_UsesBaseSpeedForShortOverflow()
    {
        Assert.AreEqual(40d, MarqueeBehavior.CalculateMarqueeSpeed(0));
        Assert.AreEqual(40d, MarqueeBehavior.CalculateMarqueeSpeed(-20));
    }

    [TestMethod]
    public void CalculateMarqueeSpeed_IncreasesForLongerOverflow() => Assert.IsGreaterThan(
            MarqueeBehavior.CalculateMarqueeSpeed(100),
            MarqueeBehavior.CalculateMarqueeSpeed(500));

    [TestMethod]
    public void CalculateMarqueeSpeed_IsCapped() => Assert.AreEqual(240d, MarqueeBehavior.CalculateMarqueeSpeed(2000));
}
