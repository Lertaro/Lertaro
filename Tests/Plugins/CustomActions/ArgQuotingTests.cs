namespace Lertaro.Plugins.CustomActions.Tests;

// Byte-for-byte the same implementation as CustomCommands' own ArgQuoting.cs -- deliberately
// duplicated per plugin (see that file's own doc comment), so this copy gets the same full coverage
// rather than being assumed to behave identically forever.
[TestClass]
public sealed class ArgQuotingTests
{
    [TestMethod]
    public void Quote_NoSpecialChars_ReturnsValueUnchanged() => Assert.AreEqual("hello", ArgQuoting.Quote("hello"));

    [TestMethod]
    public void Quote_EmptyString_IsWrappedInQuotes() => Assert.AreEqual("\"\"", ArgQuoting.Quote(""));

    [TestMethod]
    public void Quote_ContainsSpace_IsWrappedInQuotes() => Assert.AreEqual("\"hello world\"", ArgQuoting.Quote("hello world"));

    [TestMethod]
    public void Quote_ContainsQuote_EscapesIt() => Assert.AreEqual("\"say \\\"hi\\\"\"", ArgQuoting.Quote("say \"hi\""));

    [TestMethod]
    public void Quote_TrailingBackslashImmediatelyBeforeClosingQuote_IsDoubled()
    {
        var value = "a " + "\\";
        var result = ArgQuoting.Quote(value);

        var expected = "\"" + "a " + "\\" + "\\" + "\"";
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Quote_BackslashesImmediatelyBeforeAQuoteChar_AreDoubledPlusOne()
    {
        var result = ArgQuoting.Quote("a\\\"b c");

        Assert.AreEqual("\"a\\\\\\\"b c\"", result);
    }

    [TestMethod]
    public void Quote_OrdinaryBackslashesNotBeforeQuoteOrEnd_AreLeftAsIs()
    {
        var result = ArgQuoting.Quote(@"C:\Program Files\App");

        Assert.AreEqual("\"C:\\Program Files\\App\"", result);
    }

    [TestMethod]
    public void Quote_SingleTrailingBackslashWithNoSpace_LeftAsIs() =>
        Assert.AreEqual(@"C:\dir\", ArgQuoting.Quote(@"C:\dir\"));
}
