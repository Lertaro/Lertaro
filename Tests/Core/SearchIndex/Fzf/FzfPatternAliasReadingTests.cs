using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// "A transliteration is read both ways; the candidate's own text is read the one way the user typed."
//
// The reported gap: with fuzzy matching off, a pinyin abbreviation that mixes an initial with a whole
// reading ("gxiao" = 高's g + 效's xiao) reached 0.3% of the names it spells out, against 76% with fuzzy
// on -- it is never a contiguous run of any alias, so the strict reading alone could not see it. These
// cover the alias-side view that closes it, and the two things it must NOT do: loosen the candidate's own
// name, and let a match start inside a syllable.
//
// Flipping SearchContext.FuzzyMatchEnabled is process-wide, so this class is non-parallelizable (see
// FzfPatternTests, which carries the attribute for the same reason).
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternAliasReadingTests
{
    private const char Sep = (char)2;

    private static FzfTermKind[] Kinds(FzfPattern pattern, int set = 0)
        => pattern.TermSets[set].Terms.Select(term => term.Kind).ToArray();

    [TestMethod]
    public void AliasReading_OffersTheContiguousRunFirstAndTheSubsequenceSecond()
    {
        foreach (var fuzzy in new[] { false, true })
        {
            SearchContext.FuzzyMatchEnabled = fuzzy;
            try
            {
                // The order is load-bearing, not cosmetic: with the loose reading first, "jt" matches wide
                // across a long alias, the quality gate rejects that match, and the tight "jt" run inside
                // 出版集团's initials alias is never examined.
                CollectionAssert.AreEqual(
                    new[] { FzfTermKind.Exact, FzfTermKind.Fuzzy },
                    Kinds(FzfPattern.Parse("gxiao").AliasReading),
                    $"fuzzy={fuzzy}");
            }
            finally
            {
                SearchContext.FuzzyMatchEnabled = true;
            }
        }
    }

    [TestMethod]
    [DataRow("^gx")]
    [DataRow("gx$")]
    [DataRow("'gx'")]
    public void AliasReading_LeavesAnOperatorsOwnKindAlone(string query)
    {
        // An operator says exactly what it means, so there is no second reading to offer.
        var pattern = FzfPattern.Parse(query);
        var own = Kinds(pattern);

        CollectionAssert.AreEqual(own, Kinds(pattern.AliasReading), query);
        Assert.HasCount(1, own, query);
    }

    [TestMethod]
    public void AliasReading_LeavesAnExclusionAlone()
    {
        // "!term" is only ever compared against the candidate's own name (see FzfPatternExclusionExtensions),
        // so a second reading would be an instruction nothing reads.
        var terms = FzfPattern.Parse("!gx").AliasReading.TermSets[0].Terms;

        Assert.HasCount(1, terms);
        Assert.AreEqual(FzfTermKind.Exact, terms[0].Kind);
        Assert.IsTrue(terms[0].Inverse);
    }

    [TestMethod]
    public void AliasReading_RequiresAlignmentInBothModes()
    {
        // A transliteration is a spelling of the candidate, and no spelling of a sound starts in the middle
        // of that sound -- so the alignment rule is a property of the alias tiers, not of the fuzzy setting.
        // It used to be carried over from the source terms, which turned it off whenever fuzzy matching was
        // on: "huchu" (the middle of 出's "chu") reached 简单输出式 with fuzzy on and not with it off.
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            Assert.IsTrue(FzfPattern.Parse("gxiao").AliasReading.RequiresAlignedAliases);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }

        Assert.IsTrue(FzfPattern.Parse("gxiao").AliasReading.RequiresAlignedAliases, "with fuzzy on as well");
        Assert.IsTrue(FzfPattern.Parse("huchu").RequiresAlignedAliases);
    }

    [TestMethod]
    public void AliasReading_FindsAnAbbreviationTheStrictReadingCannot()
    {
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            var pattern = FzfPattern.Parse("gxiao");
            var alias = $"gao{Sep}xiao{Sep}neng".AsSpan();

            Assert.IsFalse(pattern.TryMatch(alias, out _, FzfScoringScheme.Default), "no alias contains 'gxiao' as a run");
            Assert.IsTrue(pattern.AliasReading.TryMatch(alias, out var match, FzfScoringScheme.Default));
            Assert.AreEqual(0, match.MinBegin, "the abbreviation starts at 高, which is a syllable start");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void AliasReading_StillLeavesASpliceForTheAlignmentRuleToRefuse()
    {
        // The view decides WHAT can match, never WHERE it may start: "ex" now finds 学习's xue+xi loosely, and
        // the match's own start (inside xue) is what AliasMatchRules refuses. The two are applied together at
        // every alias call site, which is why this pair is asserted here rather than the rejection alone.
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            var pattern = FzfPattern.Parse("ex");
            var alias = $"xue{Sep}xi".AsSpan();

            Assert.IsTrue(pattern.AliasReading.TryMatch(alias, out var match, FzfScoringScheme.Default));
            Assert.IsFalse(
                AliasMatchRules.AllowsMatch(pattern.RequiresAlignedAliases, Sep, alias, match.MinBegin),
                "the 'e' of the splice sits inside xue");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    [TestMethod]
    public void ReadingFor_AProviderWithoutDeclaredStructureKeepsTheStrictReading()
    {
        // With no boundary to align to, the loose reading can no longer tell a mixed abbreviation from a
        // splice, so a provider that declares no separator is left exactly as it was.
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            var pattern = FzfPattern.Parse("gxiao");
            var alias = $"gao{Sep}xiao".AsSpan();

            Assert.IsFalse(pattern.ReadingFor('\0').TryMatch(alias, out _, FzfScoringScheme.Default));
            Assert.IsTrue(pattern.ReadingFor(Sep).TryMatch(alias, out _, FzfScoringScheme.Default));
            Assert.AreSame(pattern, pattern.ReadingFor('\0'), "the strict reading is the query itself");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }
}
