using System.Globalization;
using Lertaro.App.Converters;

namespace Lertaro.App.Tests.Converters;

[TestClass]
public sealed class SplitColumnsConverterTests
{
    private static readonly SplitColumnsConverter Converter = new();

    [TestMethod]
    public void Convert_TabSeparatedString_SplitsIntoParts()
    {
        var result = (string[])Converter.Convert("a\tb\tc", typeof(string[]), null!, CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
    }

    [TestMethod]
    public void Convert_NoTabs_ReturnsSingleElementArray()
    {
        var result = (string[])Converter.Convert("abc", typeof(string[]), null!, CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { "abc" }, result);
    }

    [TestMethod]
    public void Convert_EmptyTabSegments_AreRemoved()
    {
        var result = (string[])Converter.Convert("a\t\tb", typeof(string[]), null!, CultureInfo.InvariantCulture);

        CollectionAssert.AreEqual(new[] { "a", "b" }, result);
    }

    [TestMethod]
    public void Convert_NonStringValue_ReturnsEmptyArray()
    {
        var result = (string[])Converter.Convert(42, typeof(string[]), null!, CultureInfo.InvariantCulture);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void ConvertBack_Throws() =>
        Assert.ThrowsExactly<NotImplementedException>(() => Converter.ConvertBack("x", typeof(string), null!, CultureInfo.InvariantCulture));
}
