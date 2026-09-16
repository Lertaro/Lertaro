using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// The ':' exclusion operator that replaced the old '!' prefix: ":temp" drops every candidate whose file
// name contains "temp".
//
// Three separate things have to hold for that to be true end to end, and each is pinned below:
//   1. the parser turns ":term" into an Inverse, always-Exact term (never a literal ":term", never fuzzy);
//   2. the byte path -- the one that actually RUNS for pure-ASCII names -- honours the same inverse
//      semantics while still being guarded against the exclusion-only query;
//   3. an exclusion-only query matches NOTHING rather than everything. This is the non-obvious one: an
//      inverse term is satisfied by the ABSENCE of its text, so a lone ":temp" would otherwise leave every
//      candidate satisfying it and show the whole index.
//
// The alias forms an alias provider would add are irrelevant here (none is registered in this assembly),
// but the parser must still SKIP that expansion for an inverse term -- a pinyin spelling must not be able
// to exclude a file the user never named.
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternExclusionTests
{
    [TestMethod]
    public void Parse_ColonPrefix_MarksTheTermInverse()
    {
        var term = FzfPattern.Parse(":temp").TermSets[0].Terms[0];

        Assert.IsTrue(term.Inverse);
        Assert.AreEqual("temp", term.Text);
    }

    [TestMethod]
    public void Parse_ColonPrefix_IsAlwaysExact_EvenWithFuzzyEnabled()
    {
        // An exclusion states what must NOT be there; a loose subsequence reading would reject far more
        // than the user named, so the fuzzy setting must not loosen it.
        var previous = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = true;
        try
        {
            Assert.AreEqual(FzfTermKind.Exact, FzfPattern.Parse(":temp").TermSets[0].Terms[0].Kind);
        }
        finally { SearchContext.FuzzyMatchEnabled = previous; }
    }

    [TestMethod]
    public void Parse_ColonPrefix_DoesNotAddAliasForms()
    {
        var set = FzfPattern.Parse(":temp").TermSets[0];

        Assert.HasCount(1, set.Terms);
    }

    [TestMethod]
    public void Parse_MixedWithAPositiveTerm_KeepsOneSetPerWord()
    {
        var pattern = FzfPattern.Parse("report :temp");

        Assert.HasCount(2, pattern.TermSets);
        Assert.IsFalse(pattern.TermSets[0].Terms[0].Inverse);
        Assert.AreEqual("report", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TermSets[1].Terms[0].Inverse);
    }

    [TestMethod]
    public void Parse_BareColon_IsDroppedRatherThanBecomingAnEmptyTerm()
    {
        // A lone ":" is not an exclusion of nothing -- dropping the word keeps a stray colon from turning
        // the query into one that can never match.
        Assert.IsTrue(FzfPattern.Parse(":").IsEmpty);
    }

    [TestMethod]
    public void Parse_TrailingBareColon_LeavesThePositiveTermAlone()
    {
        // The same word-level drop has to hold mid-query: ":term" is an exclusion, a bare ":" is nothing.
        var pattern = FzfPattern.Parse("report :");

        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("report", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("report.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_DriveSpecIsNotMistakenForAnExclusion()
    {
        // "d:" is letter-then-colon (a drive); ":temp" is colon-first (an exclusion). The two rules share
        // the colon but can never collide.
        var pattern = FzfPattern.Parse("d: report");

        Assert.AreEqual("d", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.IsFalse(pattern.TermSets[0].Terms[0].Inverse);
    }

    [TestMethod]
    public void Parse_TrailingColonInsideAWord_StaysLiteralText()
    {
        var pattern = FzfPattern.Parse("c:\\path");

        Assert.IsFalse(pattern.TermSets[0].Terms[0].Inverse);
        Assert.AreEqual("c:\\path", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void GetTotalTermLength_IgnoresTheExcludedTerm()
    {
        // Only the positive term is text the user actually typed for the match quality gate to scale by.
        Assert.AreEqual("report".Length, FzfPattern.Parse("report :temp").GetTotalTermLength());
    }

    [TestMethod]
    public void TryMatch_PositiveTermWithAnExclusion_RejectsNamesContainingIt()
    {
        var pattern = FzfPattern.Parse("report :temp");

        Assert.IsTrue(pattern.TryMatch("report-final.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("report-temp.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_OrWithExclusion_IsIndependentOfTermOrder()
    {
        // The set is a disjunction, so a negative alternative is satisfied by the text being ABSENT and
        // must not veto a positive alternative that does match. Both readings are pinned explicitly:
        // "report-temp.txt" is satisfied through "report", "notes.txt" through the absence of "temp",
        // and "draft-temp.txt" through neither.
        foreach (var pattern in new[] { FzfPattern.Parse("report | :temp"), FzfPattern.Parse(":temp | report") })
        {
            Assert.IsTrue(pattern.TryMatch("report-temp.txt", out _, FzfScoringScheme.Default));
            Assert.IsTrue(pattern.TryMatch("notes.txt", out _, FzfScoringScheme.Default));
            Assert.IsFalse(pattern.TryMatch("draft-temp.txt", out _, FzfScoringScheme.Default));
        }
    }

    [TestMethod]
    public void TryMatch_ExclusionOnly_MatchesNothing()
    {
        var pattern = FzfPattern.Parse(":temp");

        Assert.IsFalse(pattern.HasPositiveTerm);
        Assert.IsFalse(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("temp.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_TwoExclusionsOnly_MatchesNothing()
    {
        Assert.IsFalse(FzfPattern.Parse(":temp :log").TryMatch("readme.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_RegexOnlyQueryWithNoPositiveTerm_StillMatchesOnTheRegex()
    {
        // The exclusion-only guard must not swallow a regex-only query: its "positive" requirement is the
        // regex itself, which is why the guard is skipped when regex clauses are present.
        var pattern = FzfPattern.Parse("regex:/^ab\\.txt$/");

        Assert.IsFalse(pattern.HasPositiveTerm);
        Assert.IsTrue(pattern.TryMatch("ab.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("zz.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void BytePattern_PositiveTermWithAnExclusion_RejectsNamesContainingIt()
    {
        var pattern = FzfBytePattern.From(FzfPattern.Parse("report :temp"));

        Assert.IsTrue(TryMatchBytes(pattern, "report-final.txt"));
        Assert.IsFalse(TryMatchBytes(pattern, "report-temp.txt"));
    }

    [TestMethod]
    public void BytePattern_OrWithExclusion_IsIndependentOfTermOrder()
    {
        // Same three readings as the char-path test above, over the ASCII fast path.
        var leftFirst = FzfBytePattern.From(FzfPattern.Parse("report | :temp"));
        var exclusionFirst = FzfBytePattern.From(FzfPattern.Parse(":temp | report"));

        Assert.IsTrue(TryMatchBytes(leftFirst, "report-temp.txt"));
        Assert.IsTrue(TryMatchBytes(exclusionFirst, "report-temp.txt"));
        Assert.IsTrue(TryMatchBytes(leftFirst, "notes.txt"));
        Assert.IsTrue(TryMatchBytes(exclusionFirst, "notes.txt"));
        Assert.IsFalse(TryMatchBytes(leftFirst, "draft-temp.txt"));
        Assert.IsFalse(TryMatchBytes(exclusionFirst, "draft-temp.txt"));
    }

    [TestMethod]
    public void BytePattern_ExclusionOnly_MatchesNothing()
    {
        // The byte path is what runs for a pure-ASCII name and it returns a hit before the char path is
        // ever consulted, so this guard is not redundant with the char-side one.
        var pattern = FzfBytePattern.From(FzfPattern.Parse(":temp"));

        Assert.IsFalse(TryMatchBytes(pattern, "readme.txt"));
        Assert.IsFalse(TryMatchBytes(pattern, "temp.txt"));
    }

    private static bool TryMatchBytes(FzfBytePattern pattern, string text)
        => pattern.TryMatch(Encoding.ASCII.GetBytes(text), out _, FzfScoringScheme.Default, new FzfSlab(), new FzfByteBuffers());
}
