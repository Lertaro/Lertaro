using Lertaro.App.Views.SpaceAnalyzer;

namespace Lertaro.App.Tests.Views.SpaceAnalyzer;

[TestClass]
public sealed class SpaceTreemapPresenterTests
{
    [TestMethod]
    [DataRow(0L, 100L, 0.34)]
    [DataRow(25L, 100L, 0.64)]
    [DataRow(100L, 100L, 0.94)]
    [DataRow(200L, 100L, 0.94)]
    [DataRow(50L, 0L, 0.34)]
    public void CalculateAccentAmount_MapsRelativeSizeToVisibleRange(long size, long largestSize, double expected)
        => Assert.AreEqual(expected, SpaceTreemapPresenter.CalculateAccentAmount(size, largestSize), 0.0001);
}
