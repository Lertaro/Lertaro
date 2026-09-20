using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// The alias tier's half of the ':' exclusion contract: ":term" is a statement about the candidate's
// NAME, so it has to KEEP reading the name when the positive terms are satisfied by an alias.
//
// Matching the alias on its own -- what every tier used to do -- cannot see the exclusion at all: a
// pinyin or simplified alias is exactly the text that does not contain the excluded characters, so the
// "absent" test succeeded for every candidate and the exclusion stopped filtering anything. The real
// shape of that: "zghsy :演唱会" (initials of 中国好声音) returned 中国好声音演唱会.txt.
//
// No alias provider is registered in this assembly, so the alias text is written out by hand here.
// That is deliberate: these pin the matcher's rule, not any one provider's output.
[TestClass]
public sealed class FzfPatternAliasExclusionTests
{
    // What the pinyin provider bakes for 中国好声音演唱会.txt, initials only.
    private const string Alias = "zghsyych";

    private static bool Matches(string query, string alias, string name)
        => FzfPattern.Parse(query).TryMatchAlias(alias, name, out _, FzfScoringScheme.Default);

    [TestMethod]
    public void TryMatchAlias_ExclusionReadsTheName_NotTheAlias()
    {
        // The whole bug in one test: the positive term is satisfied by the alias, the excluded text is
        // in the name, and the file must therefore NOT come back.
        Assert.IsFalse(Matches("zghsy :演唱会", Alias, "中国好声音演唱会.txt"));
        Assert.IsTrue(Matches("zghsy :演唱会", Alias, "中国好声音采访.txt"));
    }

    [TestMethod]
    public void TryMatch_SameAliasAsPlainText_CannotSeeTheExclusion()
    {
        // Pins WHY TryMatchAlias exists rather than a comment: handed the alias as if it were the
        // candidate's text, the pattern matches -- the exclusion is satisfied by the alias's own silence.
        // Any caller that reverts to TryMatch(alias) gets this answer back, which is the reported bug.
        var pattern = FzfPattern.Parse("zghsy :演唱会");

        Assert.IsTrue(pattern.TryMatch(Alias, out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatchAlias(Alias, "中国好声音演唱会.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatchAlias_ExclusionOnlyVetoesItsOwnText()
    {
        // The other direction, so the fix cannot pass by rejecting everything: an alias hit survives an
        // exclusion naming text this candidate's name does not carry.
        Assert.IsTrue(Matches("zghsy :采访", Alias, "中国好声音演唱会.txt"));
    }

    [TestMethod]
    public void TryMatchAlias_NameThatCarriesThePositiveTermToo_IsStillVetoed()
    {
        // The name matches the positive term literally, so the name tier already rejected it -- and the
        // alias tier must not quietly undo that. Both aliases here carry "zghsy" themselves.
        Assert.IsFalse(Matches("zghsy :演唱会", "zghsyych.txt", "zghsy演唱会.txt"));
        Assert.IsTrue(Matches("zghsy :演唱会", "zghsycf.txt", "zghsy采访.txt"));
    }

    [TestMethod]
    public void TryMatchAlias_OrSet_KeepsItsOwnOrSemantics()
    {
        // "report | :temp" is ONE set of alternatives, so it reads as "carries report, or does not carry
        // temp" -- self-contained tests for that live in FzfPatternExclusionTests. What matters here is
        // that the alias does not change which side answers: the positive alternative is tried against
        // the alias, the exclusion against the name, and either one satisfies the set.
        Assert.IsTrue(Matches("report | :temp", "report-xyz", "draft-temp.txt"), "the positive alternative matched the alias");
        Assert.IsTrue(Matches("report | :temp", "draft-xyz", "draft-final.txt"), "the exclusion is satisfied: the name has no 'temp'");
        Assert.IsFalse(Matches("report | :temp", "draft-xyz", "draft-temp.txt"), "neither alternative holds");
    }

    [TestMethod]
    public void TryMatchAlias_AndFirstGroups_KeepTheExclusionOnTheName()
    {
        // "report | summary :temp" is the DNF shape: report OR (summary AND not temp). The exclusion sits
        // inside the second group and must still veto it when the name carries "temp", while the first
        // group keeps answering from the alias alone.
        Assert.IsFalse(Matches("report | summary :temp", "summary-xyz", "draft-temp.txt"));
        Assert.IsTrue(Matches("report | summary :temp", "summary-xyz", "draft-final.txt"));
        Assert.IsTrue(Matches("report | summary :temp", "report-xyz", "draft-temp.txt"));
        Assert.IsFalse(Matches("report | summary :temp", "other-xyz", "draft-temp.txt"));
    }

    [TestMethod]
    public void HasExclusions_TracksTheQueryShape()
    {
        // The byte-level alias matcher steps aside on this flag (it has no bytes for a non-ASCII
        // exclusion), so it has to be right for both the flat and the DNF shape.
        Assert.IsTrue(FzfPattern.Parse("zghsy :演唱会").HasExclusions);
        Assert.IsTrue(FzfPattern.Parse("report | summary :temp").HasExclusions);
        Assert.IsFalse(FzfPattern.Parse("zghsy").HasExclusions);
        Assert.IsFalse(FzfPattern.Parse("zghsy /演唱会/").HasExclusions);
    }
}
