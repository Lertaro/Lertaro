using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// Some of these flip SearchContext.DefaultFuzzyMatchEnabled, which is one process-wide field rather
// than a per-flow one -- while this assembly runs test METHODS in parallel. Anything that parses a
// query during that window reads whichever value it happens to see, so a handful of unrelated tests
// failed at random and never the same ones twice. MSTest runs non-parallelizable tests after the
// parallel batch, so this keeps the flip from overlapping anything.
//
// Reproduced before fixing, by holding the flip open for three seconds: four unrelated tests failed in
// one run, none in the next. AliasHighlightTests in the App project carries this attribute for exactly
// the same reason.
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternTests
{
    [TestMethod]
    public void Parse_EmptyQuery_IsEmpty() => Assert.IsTrue(FzfPattern.Parse("").IsEmpty);

    [TestMethod]
    public void Parse_DriveLetterTerm_ExtractsTargetDriveAndDropsItFromTerms()
    {
        var pattern = FzfPattern.Parse("c: readme");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
    }

    // Only the bare "c:" token is a drive now. "c:readme" is NOT -- the colon stays literal, because
    // guessing a drive out of the first two characters produced false positives on real names containing
    // a colon, and Windows file names cannot contain one anyway.
    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_IsNotADrive()
    {
        var pattern = FzfPattern.Parse("c:readme");

        Assert.IsNull(pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("c:readme", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterWithAndWithoutASpace_NowDiffer()
    {
        // The space is what separates the drive spec from the query -- without it there is no drive.
        var spaced = FzfPattern.Parse("c: readme report");
        var joined = FzfPattern.Parse("c:readme report");

        Assert.AreEqual("c", spaced.TargetDrive);
        Assert.IsNull(joined.TargetDrive);
    }

    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_KeepsTheColonInTheTerm()
    {
        var pattern = FzfPattern.Parse("c:readme report");

        Assert.IsNull(pattern.TargetDrive);
        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual("c:readme", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("report", pattern.TermSets[1].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterAlone_HasNoTerms()
    {
        var pattern = FzfPattern.Parse("c:");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.IsEmpty(pattern.TermSets);
    }

    [TestMethod]
    public void Parse_LastDriveTokenWins()
    {
        var pattern = FzfPattern.Parse("c: d: readme");

        Assert.AreEqual("d", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_MatchesSubsequence()
    {
        var pattern = FzfPattern.Parse("rdm");

        var matched = pattern.TryMatch("readme.md", out var result, FzfScoringScheme.Default);

        Assert.IsTrue(matched);
        Assert.IsTrue(result.ValidOffsetFound);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_FailsWhenSubsequenceAbsent()
    {
        var pattern = FzfPattern.Parse("xyz");

        var matched = pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default);

        Assert.IsFalse(matched);
    }

    [TestMethod]
    public void TryMatch_MultipleTerms_RequiresEveryTermToMatch()
    {
        var pattern = FzfPattern.Parse("read md");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_ExclamationPrefix_IsLiteralTextNotAnExclusion()
    {
        var pattern = FzfPattern.Parse("read !md");

        // '!' is no longer an operator, so this is two ANDed literal terms and "readme.txt" fails it.
        Assert.IsTrue(pattern.TryMatch("read !md.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_CaretPrefix_IsLiteralTextNotAPrefixAnchor()
    {
        var pattern = FzfPattern.Parse("^read");

        // The caret is literal now, so it must actually appear in the name.
        Assert.IsTrue(pattern.TryMatch("^readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("unread.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_DollarSuffix_IsLiteralTextNotASuffixAnchor()
    {
        var pattern = FzfPattern.Parse("md$");

        // "md5sum.txt" contains the literal "md$"? No -- so this no longer matches at all.
        Assert.IsFalse(pattern.TryMatch("md5sum.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_CaretDollarPair_IsLiteralTextNotAWholeMatch()
    {
        var pattern = FzfPattern.Parse("^readme.md$");

        // Not the old whole-text equality: the literal carets/dollar must appear in the name.
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    // Matching ignores case in both directions -- the query's case never makes a term case-sensitive.
    // This used to be fzf's smart case: a capital in the typed text made that term exact-case, so
    // "README" stopped matching "readme.md".
    [TestMethod]
    public void TryMatch_UpperCaseTerm_IsCaseInsensitive()
    {
        var pattern = FzfPattern.Parse("README");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_MixedCaseTerm_IsCaseInsensitive()
    {
        var pattern = FzfPattern.Parse("ReAdMe");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("README.MD", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_LowercaseTerm_IsCaseInsensitive()
    {
        var pattern = FzfPattern.Parse("readme");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
    }

    // Reachable through this API only, never from the search box: a backslash anywhere in a query
    // makes SearchQueryParser classify it as path mode, which routes to PathSearch before
    // FzfPattern.Parse is ever called.
    [TestMethod]
    public void TryMatch_EscapedSpace_IsTreatedAsLiteralSpaceInOneTerm()
    {
        var pattern = FzfPattern.Parse(@"my\ file");

        Assert.IsTrue(pattern.TryMatch("my file.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("myfile.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_MatchesIfEitherSegmentMatches()
    {
        var pattern = FzfPattern.Parse("he");

        // "he" and "hu" are alternate readings of the same alias, joined with '|' at the text side.
        Assert.IsTrue(pattern.TryMatch("he|hu|huo", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_TermsFromDifferentSegmentsDoNotCombine()
    {
        var pattern = FzfPattern.Parse("ab cd");

        // "ab" only appears in the first segment and "cd" only in the second -- a match must find
        // both terms within the SAME segment, not scattered across the whole joined string.
        Assert.IsFalse(pattern.TryMatch("ab|cd", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("abcd|xy", out _, FzfScoringScheme.Default));
    }

}
