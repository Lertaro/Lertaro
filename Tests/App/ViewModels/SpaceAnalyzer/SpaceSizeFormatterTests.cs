using Lertaro.App.ViewModels.SpaceAnalyzer;

namespace Lertaro.App.Tests.ViewModels.SpaceAnalyzer;

[TestClass]
public sealed class SpaceSizeFormatterTests
{
    [TestMethod]
    [DataRow(0L, "0 B")]
    [DataRow(1024L, "1 KB")]
    [DataRow(1572864L, "1.5 MB")]
    public void Format_UsesBinaryUnits(long bytes, string expected)
        => Assert.AreEqual(expected, SpaceSizeFormatter.Format(bytes));

    [TestMethod]
    [DataRow(25L, 100L, 25.0)]
    [DataRow(200L, 100L, 100.0)]
    [DataRow(-10L, 100L, 0.0)]
    [DataRow(10L, 0L, 0.0)]
    public void RelativePercentage_ClampsShareToProgressRange(long bytes, long totalBytes, double expected)
        => Assert.AreEqual(expected, SpaceSizeFormatter.RelativePercentage(bytes, totalBytes), 0.0001);
}
