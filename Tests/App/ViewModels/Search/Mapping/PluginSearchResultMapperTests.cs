using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class PluginSearchResultMapperTests
{
    [TestMethod]
    public void SanitizeSingleLine_NullOrEmpty_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, PluginSearchResultMapper.SanitizeSingleLine(null));
        Assert.AreEqual(string.Empty, PluginSearchResultMapper.SanitizeSingleLine(string.Empty));
    }

    [TestMethod]
    public void SanitizeSingleLine_ReplacesNewlinesWithSpaces()
    {
        var input = "Line1\r\nLine2\nLine3\rLine4";
        var result = PluginSearchResultMapper.SanitizeSingleLine(input);

        Assert.AreEqual("Line1 Line2 Line3 Line4", result);
    }

    [TestMethod]
    public void SanitizeSingleLine_NoNewlines_ReturnsOriginal()
    {
        var input = "Single Line Text";
        var result = PluginSearchResultMapper.SanitizeSingleLine(input);

        Assert.AreEqual("Single Line Text", result);
    }
}
