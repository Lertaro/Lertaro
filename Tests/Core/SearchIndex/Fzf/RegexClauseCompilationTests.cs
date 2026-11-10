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

    // Matching nothing is right, but it is also indistinguishable from a genuine miss, so the failure has to
    // be NAMEABLE by whoever is explaining an empty result. That is a question about a query, not state some
    // earlier search left behind -- see the remarks on SearchContext.UncompilableClauses.
    [TestMethod]
    public void IsUncompilable_ReportsOnlyPatternsTheEngineRefuses()
    {
        Assert.IsTrue(RegexClauses.IsUncompilable(@"zz-probe-bad("));
        Assert.IsTrue(RegexClauses.IsUncompilable("*"));
        Assert.IsFalse(RegexClauses.IsUncompilable(@"^zz-probe-ok\.md$"));
        // Lookaround and backreferences are refused by NonBacktracking but accepted by the timed fallback,
        // so they are not user errors and must not be reported.
        Assert.IsFalse(RegexClauses.IsUncompilable(@"^(?!.*tmp).*zz-probe-alt\.md$"));
        Assert.IsFalse(RegexClauses.IsUncompilable(@"^(zz-probe-br)\1$"));
    }

    [TestMethod]
    public void IsUncompilable_AfterTheClauseHasBeenCompiled_StillReports()
    {
        // The regression this replaces: the report used to be a side effect of COMPILING, so a clause that
        // was already in the compile cache reported nothing. Typing one character back and again hits the
        // cache, and the hint vanished exactly when the user was editing the typo.
        Assert.IsFalse(RegexClauses.AllMatch([@"zz-probe-cached("], "zz-probe-cached(x"));
        Assert.IsTrue(RegexClauses.IsUncompilable(@"zz-probe-cached("));
        Assert.IsTrue(RegexClauses.IsUncompilable(@"zz-probe-cached("), "and asking twice is stable");
    }

    [TestMethod]
    public void UncompilableClauses_NamesTheBrokenOnesInQueryOrder()
    {
        // Each clause has to CLOSE with "/" or the parser leaves it as ordinary text (an unclosed clause
        // must never swallow the rest of the query), and then nothing is compiled and nothing is broken.
        var invalid = SearchContext.UncompilableClauses(@"report /zz-probe-mix(/ /\.md$/ /zz-probe-two(/");

        CollectionAssert.AreEqual(new[] { @"zz-probe-mix(", @"zz-probe-two(" }, invalid.ToList());
    }

    [TestMethod]
    public void UncompilableClauses_AnswersAboutItsOwnQueryAlone()
    {
        // Two queries the user typed in succession both have to answer for themselves: the old shared list
        // let a superseded search still running contribute ITS clause, so the hint named text that was no
        // longer in the box. There is no longer any shared state to cross.
        Assert.HasCount(1, SearchContext.UncompilableClauses(@"report /zz-probe-a(/"));
        Assert.IsEmpty(SearchContext.UncompilableClauses(@"report /zz-probe-b/"));
        Assert.HasCount(1, SearchContext.UncompilableClauses(@"report /zz-probe-a(/"),
            "and re-asking after an unrelated query gives the same answer");
    }

    [TestMethod]
    public void UncompilableClauses_NothingToReport_ForQueriesWithoutClauses()
    {
        Assert.IsEmpty(SearchContext.UncompilableClauses(null));
        Assert.IsEmpty(SearchContext.UncompilableClauses(string.Empty));
        Assert.IsEmpty(SearchContext.UncompilableClauses("plain report"));
        Assert.IsEmpty(SearchContext.UncompilableClauses(@"report /\.md$/"));
    }
}
