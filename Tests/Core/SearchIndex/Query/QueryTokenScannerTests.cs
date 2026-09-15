using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class QueryTokenScannerTests
{
    [TestMethod]
    public void Scan_PlainQuery_ReturnsItUnchangedWithNoTokens()
    {
        var result = QueryTokenScanner.Scan("readme");

        Assert.AreEqual("readme", result.Text);
        Assert.IsEmpty(result.Tokens);
    }

    [TestMethod]
    public void Scan_PluginTokenBeforeKeyword_LiftsTokenAndKeepsKeyword()
    {
        var result = QueryTokenScanner.Scan(@"\audio 报告");

        Assert.AreEqual("报告", result.Text);
        CollectionAssert.AreEqual(new[] { @"\audio" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_PluginTokenAfterKeyword_LiftsTokenAndKeepsKeyword()
    {
        var result = QueryTokenScanner.Scan(@"报告 \audio");

        Assert.AreEqual("报告", result.Text);
        CollectionAssert.AreEqual(new[] { @"\audio" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_TokenInTheMiddleOfKeyword_IsLifted()
    {
        var result = QueryTokenScanner.Scan(@"报告 \img 2024");

        Assert.AreEqual("报告 2024", result.Text);
        CollectionAssert.AreEqual(new[] { @"\img" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_MultipleTokensOfBothFamilies_AreLiftedInOrder()
    {
        var result = QueryTokenScanner.Scan(@"报告 <s>20m \audio");

        Assert.AreEqual("报告", result.Text);
        CollectionAssert.AreEqual(new[] { "<s>20m", @"\audio" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_SortTokenOnly_LeavesEmptyText()
    {
        var result = QueryTokenScanner.Scan(">c>2008.8.3");

        Assert.AreEqual(string.Empty, result.Text);
        CollectionAssert.AreEqual(new[] { ">c>2008.8.3" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_QuotedTokenLikeWord_IsTreatedAsSearchText()
    {
        var result = QueryTokenScanner.Scan(@"report ""\audio""");

        Assert.AreEqual(@"report ""\audio""", result.Text);
        Assert.IsEmpty(result.Tokens);
    }

    [TestMethod]
    public void Scan_TriggerInsideWord_IsNotAToken()
    {
        // "abc\def" -- the '\' is not the first character, so this is ordinary text.
        var result = QueryTokenScanner.Scan(@"abc\def");

        Assert.AreEqual(@"abc\def", result.Text);
        Assert.IsEmpty(result.Tokens);
    }

    [TestMethod]
    public void Scan_SingleTriggerCharacterAlone_IsNotAToken()
    {
        var result = QueryTokenScanner.Scan(@"report \");

        Assert.AreEqual(@"report \", result.Text);
        Assert.IsEmpty(result.Tokens);
    }

    [TestMethod]
    public void Scan_EscapedSpaceInsideToken_KeepsTokenWhole()
    {
        var result = QueryTokenScanner.Scan(@"report \hello\ world");

        Assert.AreEqual("report", result.Text);
        CollectionAssert.AreEqual(new[] { @"\hello world" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_CustomPluginPrefix_IsHonoured()
    {
        var result = QueryTokenScanner.Scan(@"report #audio", '#');

        Assert.AreEqual("report", result.Text);
        CollectionAssert.AreEqual(new[] { "#audio" }, result.Tokens.ToArray());
    }

    [TestMethod]
    public void Scan_EmptyQuery_ReturnsEmpty()
    {
        var result = QueryTokenScanner.Scan(string.Empty);

        Assert.AreEqual(string.Empty, result.Text);
        Assert.IsEmpty(result.Tokens);
    }

    [TestMethod]
    public void Scan_ColonIsNotATokenTrigger()
    {
        // ':' is the exclusion operator, not a token prefix -- it must survive scanning untouched.
        var result = QueryTokenScanner.Scan(@"报告 :temp");

        Assert.AreEqual(@"报告 :temp", result.Text);
        Assert.IsEmpty(result.Tokens);
    }
}
