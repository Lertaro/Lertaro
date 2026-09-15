using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// Split out from FzfPatternTests to keep the test files under the repository's 300-line limit. These
// tests cover phrase parsing, fuzzy-mode switches, and term-length bookkeeping for FzfPattern.
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternParsingTests
{
    [TestMethod]
    public void TryMatch_QuotedPhraseContainingSpaces_IsOneBoundaryTerm()
    {
        var pattern = FzfPattern.Parse("'cad acb'");

        Assert.HasCount(1, pattern.TermSets);
        Assert.HasCount(1, pattern.TermSets[0].Terms);
        Assert.AreEqual(FzfTermKind.ExactBoundary, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("cad acb", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("cad-acb.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_NegatedQuotedPhraseContainingSpaces_IsOneInverseBoundaryTerm()
    {
        var pattern = FzfPattern.Parse("txt !'cad acb'");

        Assert.IsTrue(pattern.TryMatch("other.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_ApostropheInsideWord_DoesNotOpenAQuotedPhrase()
    {
        var pattern = FzfPattern.Parse("don't stop");

        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual("don't", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("stop", pattern.TermSets[1].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_UnmatchedOpeningQuote_KeepsTermByTermReading()
    {
        var pattern = FzfPattern.Parse("'cad acb");

        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("cad", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("acb", pattern.TermSets[1].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_OrOfQuotedTerms_KeepsTheSeparatorInsteadOfMergingIntoOnePhrase()
    {
        var pattern = FzfPattern.Parse("'foo | 'bar'");

        Assert.HasCount(1, pattern.TermSets);
        Assert.HasCount(2, pattern.TermSets[0].Terms);
        Assert.AreEqual("foo", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("bar", pattern.TermSets[0].Terms[1].Text);
    }

    [TestMethod]
    public void Parse_ExactMarkerBeforeEndAnchor_KeepsSuffixSemantics()
    {
        var pattern = FzfPattern.Parse("'md$");

        Assert.AreEqual(FzfTermKind.Suffix, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("md", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("md5sum.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_PrefixMarkerFollowedByExactMarker_DropsTheRedundantQuote()
    {
        var pattern = FzfPattern.Parse("^'read");

        Assert.AreEqual(FzfTermKind.Prefix, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("read", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_EscapedSpaceInsideQuotedPhrase_StillParsesAsOneTerm()
    {
        var pattern = FzfPattern.Parse(@"'cad\ acb'");

        Assert.AreEqual(FzfTermKind.ExactBoundary, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("cad acb", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    }

    private static void WithFuzzyDisabled(Action body)
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = false;
        try { body(); }
        finally { SearchContext.FuzzyMatchEnabled = previous; }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Parse_ProcessDefaultDisabled_AppliesWithoutAnyPerRequestValue()
    {
        var previous = SearchContext.DefaultFuzzyMatchEnabled;
        SearchContext.DefaultFuzzyMatchEnabled = false;
        try
        {
            var pattern = FzfPattern.Parse("ab");

            Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[0].Terms[0].Kind);
            Assert.IsFalse(pattern.TryMatch("a-b.txt", out _, FzfScoringScheme.Default));
        }
        finally { SearchContext.DefaultFuzzyMatchEnabled = previous; }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Parse_PerRequestValue_OverridesTheProcessDefault()
    {
        var previous = SearchContext.DefaultFuzzyMatchEnabled;
        SearchContext.DefaultFuzzyMatchEnabled = false;
        try
        {
            SearchContext.FuzzyMatchEnabled = true;
            try
            {
                Assert.AreEqual(FzfTermKind.Fuzzy, FzfPattern.Parse("ab").TermSets[0].Terms[0].Kind);
            }
            finally { SearchContext.FuzzyMatchEnabled = previous; }
        }
        finally { SearchContext.DefaultFuzzyMatchEnabled = previous; }
    }

    [TestMethod]
    public void Parse_FuzzyDisabled_MakesBareTermsContiguous() => WithFuzzyDisabled(() =>
    {
        var pattern = FzfPattern.Parse("ab cd");

        Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[1].Terms[0].Kind);
        Assert.IsTrue(pattern.TryMatch("ab cd.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    });

    [TestMethod]
    public void Parse_FuzzyEnabled_LeavesBareTermsFuzzy()
    {
        var pattern = FzfPattern.Parse("ab cd");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_FuzzyDisabled_ExactMarkerFlipsTheTermBackToFuzzy() => WithFuzzyDisabled(() =>
    {
        var pattern = FzfPattern.Parse("'ab");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    });

    [TestMethod]
    public void Parse_FuzzyDisabled_LeavesExplicitOperatorsAlone() => WithFuzzyDisabled(() =>
    {
        Assert.AreEqual(FzfTermKind.Prefix, FzfPattern.Parse("^read").TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Suffix, FzfPattern.Parse("md$").TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Equal, FzfPattern.Parse("^readme.md$").TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.ExactBoundary, FzfPattern.Parse("'read'").TermSets[0].Terms[0].Kind);
    });

    [TestMethod]
    public void GetTotalTermLength_SumsPositiveTermsOnlyExcludingInverse()
    {
        var pattern = FzfPattern.Parse("read !md");

        Assert.AreEqual("read".Length, pattern.GetTotalTermLength());
    }

    [TestMethod]
    public void GetTotalTermLength_CountsOneAlternativePerSet()
    {
        var pattern = FzfPattern.Parse("readme | rdm | rd");

        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme".Length, pattern.GetTotalTermLength());
    }

    [TestMethod]
    public void GetTotalTermLength_StillAddsUpAcrossSeparateTerms()
    {
        var pattern = FzfPattern.Parse("read me");

        Assert.AreEqual("read".Length + "me".Length, pattern.GetTotalTermLength());
    }

    private static void WithOrFirstPrecedence(Action body)
    {
        var previous = SearchContext.AndFirstPrecedence;
        SearchContext.AndFirstPrecedence = false;
        try { body(); }
        finally { SearchContext.AndFirstPrecedence = previous; }
    }

    [TestMethod]
    public void Parse_AndFirstIsTheDefault_KeepsTheFlatShapeForAQueryMixingPipeAndSpace()
    {
        var pattern = FzfPattern.Parse("report | summary 2024");

        Assert.IsNotNull(pattern.OrGroups);
        Assert.HasCount(2, pattern.OrGroups);
        Assert.HasCount(1, pattern.OrGroups[0].Terms);
        Assert.HasCount(2, pattern.OrGroups[1].Terms);
        Assert.AreEqual("report", pattern.OrGroups[0].Terms[0].Text);
        Assert.AreEqual("summary", pattern.OrGroups[1].Terms[0].Text);
        Assert.AreEqual("2024", pattern.OrGroups[1].Terms[1].Text);
    }

    [TestMethod]
    public void TryMatch_AndFirst_MatchesEitherTheLoneTermOrTheConjunction()
    {
        var pattern = FzfPattern.Parse("report | summary 2024");

        // report alone -- the left operand of the OR.
        Assert.IsTrue(pattern.TryMatch("report.txt", out _, FzfScoringScheme.Default));
        // summary AND 2024 -- the right operand.
        Assert.IsTrue(pattern.TryMatch("summary-2024.txt", out _, FzfScoringScheme.Default));
        // summary without 2024 fails the conjunction, and OR-first would have accepted it.
        Assert.IsFalse(pattern.TryMatch("summary.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("2024.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_OrFirst_KeepsTheHistoricalReadingOfTheSameQuery() => WithOrFirstPrecedence(() =>
    {
        var pattern = FzfPattern.Parse("report | summary 2024");

        Assert.IsNull(pattern.OrGroups);
        Assert.HasCount(2, pattern.TermSets);
        // (report OR summary) AND 2024.
        Assert.IsTrue(pattern.TryMatch("summary-2024.txt", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("report-2024.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("summary.txt", out _, FzfScoringScheme.Default));
    });

    [TestMethod]
    public void Parse_AndFirst_KeepsTheFlatShapeWhenTheQueryDoesNotMixBothOperators()
    {
        // Bare AND and bare OR mean the same thing under either reading, so both stay on the flat
        // fast path every consumer of FzfPattern already understands.
        Assert.IsNull(FzfPattern.Parse("read me").OrGroups);
        Assert.IsNull(FzfPattern.Parse("readme | rdm").OrGroups);
    }

    [TestMethod]
    public void GetTotalTermLength_AndFirst_TakesTheLongestAlternativeGroup()
    {
        var pattern = FzfPattern.Parse("read | summary 2024");

        // Groups are [read] and [summary, 2024]; the second group's terms are ANDed, so they add up,
        // and the longest group wins over the shorter alternative branch.
        Assert.AreEqual("summary".Length + "2024".Length, pattern.GetTotalTermLength());
    }

    [TestMethod]
    [DoNotParallelize]
    public void Parse_ProcessDefaultAndFirst_AppliesWithoutAnyPerRequestValue()
    {
        var previous = SearchContext.DefaultAndFirstPrecedence;
        SearchContext.DefaultAndFirstPrecedence = true;
        try
        {
            Assert.IsNotNull(FzfPattern.Parse("read | me 2").OrGroups);
        }
        finally { SearchContext.DefaultAndFirstPrecedence = previous; }
    }

    [TestMethod]
    [DoNotParallelize]
    public void Parse_PerRequestPrecedence_OverridesTheProcessDefault()
    {
        var previous = SearchContext.DefaultAndFirstPrecedence;
        SearchContext.DefaultAndFirstPrecedence = true;
        try
        {
            SearchContext.AndFirstPrecedence = false;
            try
            {
                Assert.IsNull(FzfPattern.Parse("read | me 2").OrGroups);
            }
            finally { SearchContext.AndFirstPrecedence = previous; }
        }
        finally { SearchContext.DefaultAndFirstPrecedence = previous; }
    }
}
