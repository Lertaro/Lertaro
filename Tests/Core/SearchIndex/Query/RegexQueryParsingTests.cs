using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class RegexLiteralExtractorTests
{
    [TestMethod]
    public void Extract_AlternationWithClasses_KeepsOnlyTheLiteralRun()
    {
        // The "ab" before the wildcard is required by any match.
        Assert.AreEqual("ab", RegexLiteralExtractor.ExtractRequiredLiteral("^ab.c\\..{3}$"));
    }

    [TestMethod]
    public void Extract_AlternationInThePattern_ReportsNoLiteral()
    {
        // "\.(?:ogg|mp3)$" -- "ogg" is not REQUIRED (mp3 matches without it), and the only truly required
        // piece is the escaped dot. Rather than risk returning a branch-only run, any top-level
        // alternation bails out and costs only speed.
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("\\.(?:ogg|mp3)$"));
    }

    [TestMethod]
    public void Extract_DigitClassContributesNothing()
    {
        // "[0-9]{8}_" -- the quantifier body is skipped and the class contributes nothing, leaving "_".
        Assert.AreEqual("_", RegexLiteralExtractor.ExtractRequiredLiteral("^[0-9]{8}_"));
    }

    [TestMethod]
    public void Extract_NoLiteralAtAll_ReturnsEmpty()
    {
        // Nothing here is required verbatim, so this regex cannot be prefiltered.
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral(".*"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("^.{5}$"));
        Assert.AreEqual(string.Empty, RegexLiteralExtractor.ExtractRequiredLiteral("\\d+"));
    }

    [TestMethod]
    public void Extract_LongestRunWins()
    {
        // Two runs separated by a wildcard: "ab" and "report". The longer one is the better prefilter.
        Assert.AreEqual("report", RegexLiteralExtractor.ExtractRequiredLiteral("ab.*report"));
    }

    [TestMethod]
    public void Extract_CharacterClassContentsAreNotLiteral()
    {
        // "x[abc]y": the class contributes nothing, leaving "x" and "y" as the runs, and "x" is first
        // and equally long, so "x" is reported.
        Assert.AreEqual("x", RegexLiteralExtractor.ExtractRequiredLiteral("x[abc]y"));
    }

    [TestMethod]
    public void Extract_TrailingLiteralAfterAnEndAnchor_IsStillRequired()
    {
        Assert.AreEqual("read", RegexLiteralExtractor.ExtractRequiredLiteral("^read.*\\.md$"));
    }

    [TestMethod]
    public void IsPrefixAnchored_DetectsTheCaret()
    {
        Assert.IsTrue(RegexLiteralExtractor.IsPrefixAnchored("^abc"));
        Assert.IsFalse(RegexLiteralExtractor.IsPrefixAnchored("abc"));
    }
}

[TestClass]
public sealed class RegexQueryParserTests
{
    [TestMethod]
    public void Split_RegexAlone_LeavesNoText()
    {
        var rest = RegexQueryParser.Split("regex:/^ab.c\\..{3}$/", out var clauses);

        Assert.AreEqual(string.Empty, rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual("^ab.c\\..{3}$", clauses[0].Pattern);
        Assert.AreEqual("ab", clauses[0].RequiredLiteral);
    }

    [TestMethod]
    public void Split_RegexWithTrailingWord_KeepsTheWordAsText()
    {
        var rest = RegexQueryParser.Split("regex:/^ab.c\\..{3}$/ zip", out var clauses);

        Assert.AreEqual("zip", rest);
        Assert.HasCount(1, clauses);
    }

    [TestMethod]
    public void Split_EscapedSlashInsidePattern_DoesNotEndTheClause()
    {
        var rest = RegexQueryParser.Split("regex:/a\\/b/ tail", out var clauses);

        Assert.AreEqual("tail", rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual("a\\/b", clauses[0].Pattern);
    }

    [TestMethod]
    public void Split_NoRegexClause_ReturnsTheQueryUntouched()
    {
        var rest = RegexQueryParser.Split("read me", out var clauses);

        Assert.AreEqual("read me", rest);
        Assert.IsEmpty(clauses);
    }

    [TestMethod]
    public void Split_UnterminatedRegex_StaysLiteralText()
    {
        var rest = RegexQueryParser.Split("regex:/ab", out var clauses);

        Assert.IsEmpty(clauses);
        Assert.AreEqual("regex:/ab", rest);
    }

    [TestMethod]
    public void Split_PrefixNotAtTokenBoundary_StaysLiteralText()
    {
        var rest = RegexQueryParser.Split("aregex:/ab/", out var clauses);

        Assert.IsEmpty(clauses);
        Assert.AreEqual("aregex:/ab/", rest);
    }

    [TestMethod]
    public void Split_TwoClauses_BothExtractedAndAnded()
    {
        var rest = RegexQueryParser.Split("regex:/^a/ regex:/\\.md$/ report", out var clauses);

        Assert.AreEqual("report", rest);
        Assert.HasCount(2, clauses);
    }

    [TestMethod]
    public void Split_NoExtractableLiteral_ReportsNoPrefilter()
    {
        _ = RegexQueryParser.Split("regex:/.*/", out var clauses);

        Assert.HasCount(1, clauses);
        Assert.IsFalse(clauses[0].CanPrefilter);
    }
}
