using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// How a "/.../" clause is turned into a Regex. A user types these by hand, so both of the shapes that the
// NonBacktracking engine refuses have to degrade instead of failing: an unsupported-but-legal pattern must
// fall back to a timed engine and keep matching, and an outright invalid one must match nothing rather
// than take the whole search down with an ArgumentException on every candidate.
//
// The timeout path itself is pinned by RegexTimeoutTests; these cover what happens when compilation is the
// thing that fails.
[TestClass]
public sealed class RegexClauseCompilationTests
{
    [TestMethod]
    public void AllMatch_Lookaround_PatternStillMatchesViaTheTimedFallback()
    {
        // Lookaround is unsupported under NonBacktracking, so this compiles through the fallback -- and the
        // fallback has to produce a working regex, not merely a non-throwing one.
        Assert.IsTrue(RegexClauses.AllMatch([@"^(?!.*tmp).*\.md$"], "readme.md"));
        Assert.IsFalse(RegexClauses.AllMatch([@"^(?!.*tmp).*\.md$"], "tmp.md"));
    }

    [TestMethod]
    public void AllMatch_Backreference_PatternStillMatchesViaTheTimedFallback()
    {
        Assert.IsTrue(RegexClauses.AllMatch([@"^(ab)\1$"], "abab"));
        Assert.IsFalse(RegexClauses.AllMatch([@"^(ab)\1$"], "ababab"));
    }

    [TestMethod]
    public void AllMatch_InvalidPattern_MatchesNothingInsteadOfThrowing()
    {
        // Every candidate in the search runs its clauses, so an unhandled ArgumentException here would
        // abort the search outright rather than losing one row.
        Assert.IsFalse(RegexClauses.AllMatch([@"a("], "a(b"));
        Assert.IsFalse(RegexClauses.AllMatch(["[unclosed"], "[unclosed"));
        Assert.IsFalse(RegexClauses.AllMatch(["*"], "anything"));
    }

    // The alternative reading -- drop the clause the engine cannot compile and answer on the rest --
    // would return rows the user's pattern was written to exclude, which is a wrong answer rather than
    // a missing optimization. The invalid clause therefore vetoes the query.
    [TestMethod]
    public void AllMatch_InvalidPattern_IsNotSilentlyIgnored() =>
        Assert.IsFalse(RegexClauses.AllMatch([@"a(", @"^report\.md$"], "report.md"));

    [TestMethod]
    public void Pattern_InvalidClause_IsStillAQueryButMatchesNothing()
    {
        // End to end through the pattern: the clause counts as a query (so it is not mistaken for an empty
        // one that matches everything) and answers "no" for every candidate.
        var pattern = FzfPattern.Parse(@"/a(/");

        Assert.IsFalse(pattern.IsEmpty);
        Assert.IsFalse(pattern.TryMatch("a(b", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("anything.txt", out _, FzfScoringScheme.Default));
    }

    // Matching nothing is right, but it is also indistinguishable from a genuine miss, so the failure is
    // REPORTED: the UI names the clause rather than claiming the search was simply too narrow.
    //
    // The report is process-wide and other tests compile patterns of their own, so every assertion here is
    // on a UNIQUE pattern's presence or absence rather than on the list's total contents -- MSTest gives no
    // ordering guarantee, and a test that expects an empty list would depend on nothing else having run.
    [TestMethod]
    public void InvalidPatterns_ReportsTheClauseThatCouldNotCompile()
    {
        Assert.IsFalse(RegexClauses.AllMatch([@"zz-probe-unique("], "zz-probe-unique(x"));

        CollectionAssert.Contains(RegexClauses.InvalidPatterns.ToList(), @"zz-probe-unique(");
    }

    [TestMethod]
    public void InvalidPatterns_ValidPatterns_ReportNothing()
    {
        // Every pattern here compiles, so none of them may appear in the report whatever else has.
        Assert.IsTrue(RegexClauses.AllMatch([@"^zz-probe-valid\.md$"], "zz-probe-valid.md"));
        Assert.IsFalse(RegexClauses.AllMatch([@"^(?!.*tmp).*zz-probe-valid\.md$"], "tmp.md"));

        CollectionAssert.DoesNotContain(RegexClauses.InvalidPatterns.ToList(), @"^zz-probe-valid\.md$");
        CollectionAssert.DoesNotContain(RegexClauses.InvalidPatterns.ToList(), @"^(?!.*tmp).*zz-probe-valid\.md$");
    }

    // A half-typed pattern is compiled on every keystroke, so the same broken clause is seen repeatedly.
    // It has to be named once, not once per attempt.
    [TestMethod]
    public void InvalidPatterns_SameClauseSeenTwice_ReportedOnce()
    {
        Assert.IsFalse(RegexClauses.AllMatch([@"zz-probe-dupe("], "zz-probe-dupe(x"));
        Assert.IsFalse(RegexClauses.AllMatch([@"zz-probe-dupe("], "zz-probe-dupe(xy"));

        Assert.AreEqual(1, RegexClauses.InvalidPatterns.Count(p => p == @"zz-probe-dupe("));
    }

    // A pattern the NonBacktracking engine refuses but the timed fallback accepts is NOT a failure: the
    // second attempt has to be the one that decides, or every lookaround would be reported as broken.
    [TestMethod]
    public void InvalidPatterns_FallbackSupportedPattern_IsNotReported()
    {
        Assert.IsTrue(RegexClauses.AllMatch([@"^(zz-probe-br)\1$"], "zz-probe-brzz-probe-br"));

        CollectionAssert.DoesNotContain(RegexClauses.InvalidPatterns.ToList(), @"^(zz-probe-br)\1$");
    }

    // The report is surfaced to the UI through SearchContext, which is the App-visible channel (RegexClauses
    // is internal to Core). Same list, so the two cannot drift.
    [TestMethod]
    public void SearchContext_ExposesAndClearsTheInvalidClauses()
    {
        Assert.IsFalse(RegexClauses.AllMatch([@"zz-probe-ctx("], "zz-probe-ctx(x"));

        CollectionAssert.Contains(SearchContext.InvalidRegexes.ToList(), @"zz-probe-ctx(");

        SearchContext.ClearInvalidRegexes();
        Assert.IsEmpty(SearchContext.InvalidRegexes);
    }
}
