using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QuickSearchLaunchPanelHeightCalculatorTests
{
    [TestMethod]
    public void Calculate_UsesTallestSourceAndCapsAtMaximumHeight()
    {
        var sources = new[]
        {
            new LaunchPanelSourceViewModel("small", "Small", Enumerable.Repeat(new AppSearchResult(), 1)),
            new LaunchPanelSourceViewModel("large", "Large", Enumerable.Repeat(new AppSearchResult(), 11))
        };

        var height = QuickSearchLaunchPanelHeightCalculator.Calculate(sources, 5, 522);

        Assert.AreEqual(368, height);
    }

    [TestMethod]
    public void Calculate_CapsTallestSourceAtMaximumHeight()
    {
        var source = new LaunchPanelSourceViewModel("large", "Large", Enumerable.Repeat(new AppSearchResult(), 100));

        var height = QuickSearchLaunchPanelHeightCalculator.Calculate(new[] { source }, 5, 522);

        Assert.AreEqual(522, height);
    }

    [TestMethod]
    public void Calculate_DoesNotReserveTabHeightForOneSource()
    {
        var source = new LaunchPanelSourceViewModel("single", "Single", Enumerable.Repeat(new AppSearchResult(), 1));

        var height = QuickSearchLaunchPanelHeightCalculator.Calculate(new[] { source }, 5, 522);

        Assert.AreEqual(120, height);
    }
}
