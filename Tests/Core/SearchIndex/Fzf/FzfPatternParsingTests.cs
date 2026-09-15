using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// Split out from FzfPatternTests to keep the test files under the repository's 300-line limit. These
// tests cover phrase parsing, fuzzy-mode switches, and term-length bookkeeping for FzfPattern.
//
// The historical operator prefixes ('!' "'" '^' '$') were removed in the search-syntax rewrite; the
// tests that used to pin their behavior now pin the OPPOSITE -- that they are literal text.
//
// AND-first / OR-first precedence lives in FzfPatternPrecedenceTests; this file does not repeat it.
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternParsingTests
{
    [TestMethod]
    public void Parse_ApostropheInsideWord_IsLiteralText()
    {
        var pattern = FzfPattern.Parse("don't stop");

        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual("don't", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("stop", pattern.TermSets[1].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_ApostrophePrefix_IsNoLongerExactnessFlip()
    {
        var pattern = FzfPattern.Parse("'cad");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
    }

    [TestMethod]
    public void Parse_CaretPrefix_IsNoLongerAPrefixAnchor()
    {
        var pattern = FzfPattern.Parse("^read");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
    }

    [TestMethod]
    public void Parse_DollarSuffix_IsNoLongerASuffixAnchor()
    {
        var pattern = FzfPattern.Parse("md$");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
    }

    [TestMethod]
    public void Parse_QuotedPhrase_NoLongerFormsABoundaryTerm()
    {
        // The quote characters are gone from the operator set, but the phrase merger still folds a
        // matched pair into ONE term -- that is quoting, not an operator, and it stays.
        var pattern = FzfPattern.Parse("'cad acb'");

        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("'cad acb'", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_FuzzyDisabled_LeavesEveryBareTermExact() => WithFuzzyDisabled(() =>
    {
        Assert.AreEqual(FzfTermKind.Exact, FzfPattern.Parse("^read").TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Exact, FzfPattern.Parse("md$").TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Exact, FzfPattern.Parse("'read'").TermSets[0].Terms[0].Kind);
    });

    [TestMethod]
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
    public void GetTotalTermLength_SumsEveryPositiveTerm()
    {
        var pattern = FzfPattern.Parse("read md");

        Assert.AreEqual("read".Length + "md".Length, pattern.GetTotalTermLength());
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

    [TestMethod]
    public void Parse_DriveTokenAlone_SelectsTheDrive()
        => Assert.AreEqual("d", FzfPattern.Parse("d: report").TargetDrive);

    [TestMethod]
    public void Parse_DriveTokenWithAttachedText_IsNoLongerADrive()
    {
        // "d:report" keeps the colon as literal text -- the drive rule needs the bare "d:" token.
        var pattern = FzfPattern.Parse("d:report");

        Assert.IsNull(pattern.TargetDrive);
        Assert.AreEqual("d:report", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_NonAsciiLetterBeforeColon_IsNotADrive()
    {
        var pattern = FzfPattern.Parse("中: x");

        Assert.IsNull(pattern.TargetDrive);
    }

    private static void WithFuzzyDisabled(Action body)
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = false;
        try { body(); }
        finally { SearchContext.FuzzyMatchEnabled = previous; }
    }
}
