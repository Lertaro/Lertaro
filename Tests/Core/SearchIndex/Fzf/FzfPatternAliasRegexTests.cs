using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// The other half of the contract FzfPatternAliasExclusionTests pins for ':' exclusions: a "/.../" clause
// describes characters that are really in the candidate's NAME and never reaches a match through a pinyin
// alias, so on the alias tier it has to keep reading the name even though the positive terms are being
// read off the alias.
//
// Getting this wrong fails in BOTH directions at once, which is why both are pinned: satisfied by the
// alias only -> the row is admitted on text the user's pattern never described; satisfied by the name only
// -> the row is dropped even though the alias legitimately matched. The second one is the silent killer,
// since it looks like the regex feature simply not working on Chinese names.
//
// No alias provider is registered in this assembly, so the alias text is written out by hand -- these pin
// the matcher's rule, not any one provider's output.
[TestClass]
public sealed class FzfPatternAliasRegexTests
{
    // What a pinyin provider bakes for 王菲演唱会.txt: initials only. Chosen so that a clause can be
    // satisfiable by exactly one of the two strings -- "ych" tails the alias, "演唱会" sits in the name.
    private const string Alias = "wfych";
    private const string Name = "王菲演唱会.txt";

    private static bool MatchesAlias(string query) =>
        FzfPattern.Parse(query).TryMatchAlias(Alias, Name, out _, FzfScoringScheme.Default);

    [TestMethod]
    public void TryMatchAlias_RegexClauseRejectsWhatOnlyTheAliasSatisfies() =>
        // "wf" is reached through the alias, and the alias ends in "ych" -- but the NAME has no such run,
        // so the clause the user typed describes nothing here.
        Assert.IsFalse(MatchesAlias("wf /ych$/"), "a clause is a statement about the name, not about a spelling of it");

    [TestMethod]
    public void TryMatchAlias_RegexClauseKeepsWhatTheNameSatisfies()
    {
        // The false-negative direction, and the one that reads as the feature being broken rather than as
        // a wrong row: the name carries the clause's text, only the term is reachable through the alias.
        Assert.IsTrue(MatchesAlias("wf /演唱会/"));
        Assert.IsTrue(MatchesAlias("wf /\\.txt$/"));
        Assert.IsFalse(MatchesAlias("wf /\\.md$/"), "and a clause the name fails still vetoes the row");
    }

    [TestMethod]
    public void TryMatch_NameTier_RegexClauseUnchanged()
    {
        // The name tier passes the same span as both arguments, so reading exclusionText instead of text
        // must not shift anything it used to answer.
        Assert.IsTrue(FzfPattern.Parse("report /\\.pdf$/").TryMatch("report.pdf", out _, FzfScoringScheme.Default));
        Assert.IsFalse(FzfPattern.Parse("report /\\.pdf$/").TryMatch("report.txt", out _, FzfScoringScheme.Default));
        Assert.IsTrue(FzfPattern.Parse("/^report/").TryMatch("report.txt", out _, FzfScoringScheme.Default));
    }
}
