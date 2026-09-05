namespace Lertaro.Plugins.FileUnlocker.Tests;

[TestClass]
public sealed class FileOccupationViewTests
{
    [TestMethod]
    public void CalculateMarqueeSpeed_UsesBaseSpeedForShortOverflow() => Assert.AreEqual(40d, FileOccupationView.CalculateMarqueeSpeed(0));

    [TestMethod]
    public void CalculateMarqueeSpeed_IncreasesForLongerOverflow() => Assert.IsGreaterThan(
            FileOccupationView.CalculateMarqueeSpeed(100),
            FileOccupationView.CalculateMarqueeSpeed(500));

    [TestMethod]
    public void CalculateMarqueeSpeed_IsCapped() => Assert.AreEqual(240d, FileOccupationView.CalculateMarqueeSpeed(2000));
}
