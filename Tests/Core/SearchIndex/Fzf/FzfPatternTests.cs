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

    // The space after the drive is optional. It used not to be: the drive test matched on the first two
    // characters and then dropped the WHOLE token, so "c:readme" searched drive C for nothing at all
    // while "c: readme" searched it for readme -- reported by a user who could see the two behaved
    // differently but had no way to tell why.
    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_KeepsTheRestAsATerm()
    {
        var pattern = FzfPattern.Parse("c:readme");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterWithAndWithoutASpace_AgreeExactly()
    {
        var spaced = FzfPattern.Parse("c: readme report");
        var joined = FzfPattern.Parse("c:readme report");

        Assert.AreEqual(spaced.TargetDrive, joined.TargetDrive);
        Assert.HasCount(spaced.TermSets.Length, joined.TermSets);
        for (var i = 0; i < spaced.TermSets.Length; i++)
            Assert.AreEqual(spaced.TermSets[i].Terms[0].Text, joined.TermSets[i].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_LeavesLaterTermsAlone()
    {
        var pattern = FzfPattern.Parse("c:readme report");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
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
    public void TryMatch_InverseTerm_RejectsTextContainingIt()
    {
        var pattern = FzfPattern.Parse("read !md");

        Assert.IsTrue(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_PrefixTerm_OnlyMatchesAtStart()
    {
        var pattern = FzfPattern.Parse("^read");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("unread.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_SuffixTerm_OnlyMatchesAtEnd()
    {
        var pattern = FzfPattern.Parse("md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("md5sum.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_EqualTerm_RequiresExactWholeTextMatch()
    {
        var pattern = FzfPattern.Parse("^readme.md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md.bak", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_ExactBoundaryTerm_RequiresWholeSegmentMatch()
    {
        var pattern = FzfPattern.Parse("'read'");

        // "read" is its own dot-delimited segment in "my.read.txt" -- a boundary on both sides.
        Assert.IsTrue(pattern.TryMatch("my.read.txt", out _, FzfScoringScheme.Default));
        // In "readme.md" the match would end mid-word (right before "me"), which is not a boundary.
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        // Not even a contiguous substring here.
        Assert.IsFalse(pattern.TryMatch("r-e-a-d.md", out _, FzfScoringScheme.Default));
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
    // FzfPattern.Parse is ever called. Quoting ("'my file'") is the only search-box route to a term
    // containing a space.
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
