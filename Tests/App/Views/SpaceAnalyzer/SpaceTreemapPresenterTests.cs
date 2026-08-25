using Lertaro.App.Views.SpaceAnalyzer;

namespace Lertaro.App.Tests.Views.SpaceAnalyzer;

[TestClass]
public sealed class SpaceTreemapPresenterTests
{
    [TestMethod]
    [DataRow(0L, 100L, 0.12)]
    [DataRow(25L, 100L, 0.28)]
    [DataRow(100L, 100L, 0.44)]
    [DataRow(200L, 100L, 0.44)]
    [DataRow(50L, 0L, 0.12)]
    public void CalculateAccentAmount_MapsRelativeSizeToVisibleRange(long size, long largestSize, double expected)
        => Assert.AreEqual(expected, SpaceTreemapPresenter.CalculateAccentAmount(size, largestSize), 0.0001);
}
