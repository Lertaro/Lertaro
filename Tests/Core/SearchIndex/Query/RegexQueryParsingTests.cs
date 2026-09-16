using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

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
    public void Split_NoExtractableLiteral_YieldsAnEmptyLiteral()
    {
        _ = RegexQueryParser.Split("regex:/.*/", out var clauses);

        Assert.HasCount(1, clauses);
        Assert.AreEqual(string.Empty, clauses[0].RequiredLiteral);
    }

    [TestMethod]
    public void Split_EscapedDotQuery_ReachesTheMatcherFromANonPathQuery()
    {
        var query = @"lertaro regex:/\.exe$/";

        var rest = RegexQueryParser.Split(query, out var clauses);
        var parsed = SearchQueryParser.Parse(query);
        var pattern = FzfPattern.Parse(query);

        Assert.AreEqual("lertaro", rest);
        Assert.HasCount(1, clauses);
        Assert.AreEqual(@"\.exe$", clauses[0].Pattern);
        Assert.IsFalse(parsed.IsPathMode);

        Assert.IsTrue(pattern.TryMatch("Lertaro.App.exe", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("Lertaro.App.dll", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void BytePattern_ReportsThatItCannotApplyRegexClauses()
    {
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"regex:/\.exe$/")).HasRegexClauses);
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"lertaro regex:/\.exe$/")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse("lertaro")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse(":temp")).HasRegexClauses);
    }

    [TestMethod]
    public void BytePattern_RegexOnlyQuery_DoesNotClaimAMatch()
    {
        var bytePattern = FzfBytePattern.From(FzfPattern.Parse(@"regex:/\.exe$/"));

        Assert.IsFalse(bytePattern.TryMatch(
            System.Text.Encoding.ASCII.GetBytes("a.exe"),
            out _,
            FzfScoringScheme.Default,
            new FzfSlab(),
            new FzfByteBuffers()));
    }

    [TestMethod]
    public void RequiredRegexLiteral_LongestExtractableRunWins()
    {
        Assert.AreEqual(".exe", FzfPattern.Parse(@"regex:/\.exe$/").RequiredRegexLiteral);
        Assert.AreEqual("ab", FzfPattern.Parse(@"regex:/^ab.c\..{3}$/").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_NoExtractableRun_IsEmpty()
    {
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/.*/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/^.{5}$/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"regex:/\d+/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse("plain").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_OptionalGroup_DoesNotPrefilterOutValidNames()
    {
        // Regression: an optional group used to hand its contents to the required-literal mask, so
        // "regex:/(ab)?c/" demanded "ab" and rejected "c" -- a genuine match -- before the regex ran.
        var pattern = FzfPattern.Parse(@"regex:/(ab)?c/");

        Assert.AreEqual("c", pattern.RequiredRegexLiteral);
        Assert.IsTrue(pattern.TryMatch("c", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("abc", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("ab", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void RequiredRegexLiteral_TakesTheLongestAcrossClauses()
    {
        var pattern = FzfPattern.Parse(@"regex:/ab.*/ regex:/readme/");

        Assert.AreEqual("readme", pattern.RequiredRegexLiteral);
    }
}
