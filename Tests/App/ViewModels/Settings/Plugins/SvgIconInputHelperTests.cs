using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class SvgIconInputHelperTests
{
    [TestMethod]
    public void TryConvert_MultiplePaths_ReturnsSingleLinePathData()
    {
        const string svg = "\n<svg viewBox=\"0 0 24 24\"><path d=\"M0 0\nL1 1\"/><path d=\"M2 2 L3 3\"/></svg>";

        var converted = SvgIconInputHelper.TryConvert(svg, out var pathData);

        Assert.IsTrue(converted);
        Assert.AreEqual("M0 0 L1 1 M2 2 L3 3", pathData);
    }

    [TestMethod]
    public void TryConvert_XmlDeclaration_IsAccepted()
    {
        const string svg = "<?xml version=\"1.0\"?><svg><path d=\"M0 0 L1 1\"/></svg>";

        Assert.IsTrue(SvgIconInputHelper.TryConvert(svg, out var pathData));
        Assert.AreEqual("M0 0 L1 1", pathData);
    }

    [TestMethod]
    public void TryConvert_WithoutPathData_Fails()
    {
        const string svg = "<svg><circle cx=\"1\" cy=\"1\" r=\"1\"/></svg>";

        Assert.IsFalse(SvgIconInputHelper.TryConvert(svg, out var pathData));
        Assert.AreEqual(string.Empty, pathData);
    }

    [TestMethod]
    public void TryConvert_InvalidPathData_Fails()
    {
        const string svg = "<svg><path d=\"not-a-path\"/></svg>";

        Assert.IsFalse(SvgIconInputHelper.TryConvert(svg, out var pathData));
        Assert.AreEqual(string.Empty, pathData);
    }

    [TestMethod]
    public void LooksLikeSvgDocument_PlainPathData_ReturnsFalse() => Assert.IsFalse(SvgIconInputHelper.LooksLikeSvgDocument("M0 0 L1 1"));

    [TestMethod]
    public void LooksLikeSvgDocument_XmlMarkup_ReturnsTrue() => Assert.IsTrue(SvgIconInputHelper.LooksLikeSvgDocument("<not-svg>"));

    [TestMethod]
    public void IsValidPathData_InvalidValue_ReturnsFalse() => Assert.IsFalse(SvgIconInputHelper.IsValidPathData("1"));

    [TestMethod]
    public void IsValidPathData_EmptyValue_ReturnsTrue() => Assert.IsTrue(SvgIconInputHelper.IsValidPathData(string.Empty));
}
