namespace Lertaro.Plugins.CustomCommands.Tests;

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
        // A backslash that is the value's very LAST character needs doubling so it doesn't escape
        // the closing quote CommandLineToArgvW adds -- built via concatenation, not an escaped
        // literal, so the intent (quote, 'a', space, TWO backslashes, quote) is unambiguous.
        var value = "a " + "\\"; // space forces quoting; the backslash is the last character
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
        // No whitespace/quote anywhere -> the no-quoting-needed fast path returns the value verbatim,
        // trailing backslash and all (only relevant once quoting is actually triggered).
        Assert.AreEqual(@"C:\dir\", ArgQuoting.Quote(@"C:\dir\"));
}
