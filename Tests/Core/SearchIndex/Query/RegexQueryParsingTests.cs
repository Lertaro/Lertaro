using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class RegexQueryParserTests
{
    [TestMethod]
    public void Split_RegexAlone_LeavesNoText()
    {
        var rest = RegexQueryParser.Split("/^ab.c\\..{3}$/", out var patterns);

        Assert.AreEqual(string.Empty, rest);
        Assert.HasCount(1, patterns!);
        Assert.AreEqual("^ab.c\\..{3}$", patterns![0]);
    }

    [TestMethod]
    public void Split_RegexWithTrailingWord_KeepsTheWordAsText()
    {
        var rest = RegexQueryParser.Split("/^ab.c\\..{3}$/ zip", out var patterns);

        Assert.AreEqual("zip", rest);
        Assert.HasCount(1, patterns!);
    }

    [TestMethod]
    public void Split_EscapedSlashInsidePattern_DoesNotEndTheClause()
    {
        var rest = RegexQueryParser.Split("/a\\/b/ tail", out var patterns);

        Assert.AreEqual("tail", rest);
        Assert.HasCount(1, patterns!);
        Assert.AreEqual("a\\/b", patterns![0]);
    }

    [TestMethod]
    public void Split_NoRegexClause_ReturnsTheQueryUntouched()
    {
        var rest = RegexQueryParser.Split("read me", out var patterns);

        Assert.AreEqual("read me", rest);
        Assert.IsNull(patterns, "no clauses is null, which is what FzfPattern's regexes already mean");
    }

    [TestMethod]
    public void Split_UnterminatedClause_StaysLiteralText()
    {
        var rest = RegexQueryParser.Split("/ab", out var patterns);

        Assert.IsNull(patterns);
        Assert.AreEqual("/ab", rest);
    }

    [TestMethod]
    public void Split_DelimiterInsideAWord_StaysLiteralText()
    {
        // A '/' that does not start its own word is a path separator, never a delimiter.
        var rest = RegexQueryParser.Split("a/ab/", out var patterns);

        Assert.IsNull(patterns);
        Assert.AreEqual("a/ab/", rest);
    }

    [TestMethod]
    public void Split_TwoClauses_BothExtractedAndAnded()
    {
        var rest = RegexQueryParser.Split("/^a/ /\\.md$/ report", out var patterns);

        Assert.AreEqual("report", rest);
        Assert.HasCount(2, patterns!);
    }

    // '/' is also Windows' alternate path separator, so the clause form has to be narrow enough that the
    // shapes a path actually takes are never read as patterns. These are the three that matter, and each
    // one fails a different half of the "/.../" requirement.
    [TestMethod]
    [DataRow("C:/Users/me", DisplayName = "forward-slash drive path: the '/' does not start the word")]
    [DataRow("/mnt/c/Users", DisplayName = "forward-slash absolute path: no closing delimiter")]
    [DataRow("/usr/local/", DisplayName = "multi-segment path: an unescaped '/' inside the body")]
    [DataRow("//server/share/", DisplayName = "UNC path in forward slashes")]
    [DataRow("//", DisplayName = "empty body")]
    public void Split_ForwardSlashPaths_StayLiteralText(string query)
    {
        var rest = RegexQueryParser.Split(query, out var patterns);

        Assert.IsNull(patterns, $"{query} must not be read as a pattern");
        Assert.AreEqual(query, rest);
    }

    [TestMethod]
    public void Split_EscapedSpaceInsidePattern_IsKeptInTheBody()
    {
        // The same escape convention QueryTokenScanner uses to split a query into words, so a pattern may
        // still contain a space.
        var rest = RegexQueryParser.Split(@"/^my\ file$/ tail", out var patterns);

        Assert.AreEqual("tail", rest);
        Assert.HasCount(1, patterns!);
        Assert.AreEqual(@"^my\ file$", patterns![0]);
    }

    [TestMethod]
    public void Split_ClauseFollowedByTextInTheSameWord_IsNotAClause()
    {
        // The closing delimiter has to be the word's last character, so "/a/b" is a path-looking word
        // rather than the clause "a" with a stray "b".
        var rest = RegexQueryParser.Split("/a/b", out var patterns);

        Assert.IsNull(patterns);
        Assert.AreEqual("/a/b", rest);
    }

    [TestMethod]
    public void Split_EscapedDotQuery_ReachesTheMatcherFromANonPathQuery()
    {
        var query = @"lertaro /\.exe$/";

        var rest = RegexQueryParser.Split(query, out var patterns);
        var parsed = SearchQueryParser.Parse(query);
        var pattern = FzfPattern.Parse(query);

        Assert.AreEqual("lertaro", rest);
        Assert.HasCount(1, patterns!);
        Assert.AreEqual(@"\.exe$", patterns![0]);
        Assert.IsFalse(parsed.IsPathMode);

        Assert.IsTrue(pattern.TryMatch("Lertaro.App.exe", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("Lertaro.App.dll", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void BytePattern_ReportsThatItCannotApplyRegexClauses()
    {
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"/\.exe$/")).HasRegexClauses);
        Assert.IsTrue(FzfBytePattern.From(FzfPattern.Parse(@"lertaro /\.exe$/")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse("lertaro")).HasRegexClauses);
        Assert.IsFalse(FzfBytePattern.From(FzfPattern.Parse(":temp")).HasRegexClauses);
    }

    [TestMethod]
    public void BytePattern_RegexOnlyQuery_DoesNotClaimAMatch()
    {
        var bytePattern = FzfBytePattern.From(FzfPattern.Parse(@"/\.exe$/"));

        Assert.IsFalse(bytePattern.TryMatch(
            System.Text.Encoding.ASCII.GetBytes("a.exe"),
            out _,
            FzfScoringScheme.Default,
            new FzfSlab(),
            new FzfByteBuffers()));
    }

    [TestMethod]
    public void Split_AsteriskBypassBeforeAClause_IsStrippedFirstSoTheClauseSurvives()
    {
        // The two markers are consumed by different layers and in a fixed order: the App strips the leading
        // '*' (QueryTokenScanner.StripExclusionBypass) before Core ever looks for clauses. Handing the raw
        // "*/\.md$/" to the clause reader leaves the '*' glued to the opening delimiter, so the word no
        // longer STARTS with '/' and the query silently degrades to a literal-text search.
        var stripped = QueryTokenScanner.StripExclusionBypass(@"*/\.md$/", out var bypass);
        var rest = RegexQueryParser.Split(stripped, out var patterns);

        Assert.IsTrue(bypass);
        Assert.AreEqual(string.Empty, rest);
        Assert.HasCount(1, patterns!);
        Assert.AreEqual(@"\.md$", patterns![0]);

        // The un-stripped form is not a clause at all, which is what makes the ordering load-bearing.
        RegexQueryParser.Split(@"*/\.md$/", out var unstripped);
        Assert.IsNull(unstripped);
    }

    [TestMethod]
    public void RequiredRegexLiteral_LongestExtractableRunWins()
    {
        Assert.AreEqual(".exe", FzfPattern.Parse(@"/\.exe$/").RequiredRegexLiteral);
        Assert.AreEqual("ab", FzfPattern.Parse(@"/^ab.c\..{3}$/").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_NoExtractableRun_IsEmpty()
    {
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"/.*/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"/^.{5}$/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse(@"/\d+/").RequiredRegexLiteral);
        Assert.AreEqual(string.Empty, FzfPattern.Parse("plain").RequiredRegexLiteral);
    }

    [TestMethod]
    public void RequiredRegexLiteral_OptionalGroup_DoesNotPrefilterOutValidNames()
    {
        // Regression: an optional group used to hand its contents to the required-literal mask, so
        // "/(ab)?c/" demanded "ab" and rejected "c" -- a genuine match -- before the regex ran.
        var pattern = FzfPattern.Parse(@"/(ab)?c/");

        Assert.AreEqual("c", pattern.RequiredRegexLiteral);
        Assert.IsTrue(pattern.TryMatch("c", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("abc", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("ab", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void RequiredRegexLiteral_TakesTheLongestAcrossClauses()
    {
        var pattern = FzfPattern.Parse(@"/ab.*/ /readme/");

        Assert.AreEqual("readme", pattern.RequiredRegexLiteral);
    }
}
