namespace Lertaro.Plugins.FileUnlocker.Tests;

[TestClass]
public sealed class FileOccupationViewTests
{
    // The two endpoints of the ramp are private constants of the view (Base/Max), so asserting them would
    // only restate the initializer. What the marquee actually depends on is that the speed grows with the
    // overflow it has to travel -- that is the part a refactor could break silently.
    [TestMethod]
    public void CalculateMarqueeSpeed_IncreasesForLongerOverflow() => Assert.IsGreaterThan(
            FileOccupationView.CalculateMarqueeSpeed(100),
            FileOccupationView.CalculateMarqueeSpeed(500));

    [TestMethod]
    public void CalculateMarqueeSpeed_IsCapped() => Assert.AreEqual(
            FileOccupationView.CalculateMarqueeSpeed(10_000),
            FileOccupationView.CalculateMarqueeSpeed(2_000));
}
