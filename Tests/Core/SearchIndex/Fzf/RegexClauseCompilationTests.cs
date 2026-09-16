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
}
