using Lertaro.App.ViewModels.SpaceAnalyzer;

namespace Lertaro.App.Tests.ViewModels.SpaceAnalyzer;

[TestClass]
public sealed class TreemapLayoutTests
{
    [TestMethod]
    public void Calculate_FillsContainerWithoutOverlap()
    {
        var boxes = TreemapLayout.Calculate([50, 30, 20], 100, 80);

        Assert.HasCount(3, boxes);
        Assert.AreEqual(8000, boxes.Sum(box => box.Width * box.Height), 0.001);
        foreach (var box in boxes)
        {
            Assert.IsGreaterThanOrEqualTo(0, box.X);
            Assert.IsGreaterThanOrEqualTo(0, box.Y);
            Assert.IsLessThanOrEqualTo(100.001, box.X + box.Width);
            Assert.IsLessThanOrEqualTo(80.001, box.Y + box.Height);
        }
    }

    [TestMethod]
    public void Calculate_IgnoresNonPositiveWeights()
    {
        var boxes = TreemapLayout.Calculate([10, 0, -5], 20, 20);

        Assert.HasCount(1, boxes);
        Assert.AreEqual(0, boxes[0].Index);
        Assert.AreEqual(400, boxes[0].Width * boxes[0].Height, 0.001);
    }
}
