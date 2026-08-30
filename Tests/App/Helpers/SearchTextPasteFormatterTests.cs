using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class SearchTextPasteFormatterTests
{
    [TestMethod]
    public void FormatForSearch_MultipleLines_UsesTheSharedOrSyntax() => Assert.AreEqual(
            "first | second | third",
            SearchTextPasteFormatter.FormatForSearch(" first\r\n\r\nsecond\n third "));

    [TestMethod]
    public void FormatForSearch_OnlyOneNonEmptyLine_KeepsTheFirstPhysicalLine() => Assert.AreEqual("first", SearchTextPasteFormatter.FormatForSearch("first\n\n"));

    [TestMethod]
    public void FormatForSearch_WithoutLineEnding_IsUnchanged() => Assert.AreEqual("single line", SearchTextPasteFormatter.FormatForSearch("single line"));
}
