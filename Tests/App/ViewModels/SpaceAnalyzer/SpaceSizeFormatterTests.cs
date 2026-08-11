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
}
