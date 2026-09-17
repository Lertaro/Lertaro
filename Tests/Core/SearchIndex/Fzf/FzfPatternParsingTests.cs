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

    // The precision-inversion trigger: a leading '?' takes the opposite of whatever the fuzzy-matching
    // setting says, so it is the one way to mix the two readings inside a single query. See TermTriggers.
    [TestMethod]
    public void Parse_PrecisionTrigger_WithFuzzyOn_MakesThatTermExact()
    {
        var pattern = FzfPattern.Parse("?report");

        Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("report", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("report.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("re-port.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void Parse_PrecisionTrigger_WithFuzzyOff_MakesThatTermFuzzy() => WithFuzzyDisabled(() =>
    {
        var pattern = FzfPattern.Parse("?report");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
        Assert.IsTrue(pattern.TryMatch("re-port.txt", out _, FzfScoringScheme.Default));
    });

    [TestMethod]
    public void Parse_PrecisionTrigger_AppliesToItsOwnWordOnly()
    {
        // The trigger is a property of the WORD, not of the query: only the word carrying it flips.
        var pattern = FzfPattern.Parse("?read md");

        Assert.AreEqual(FzfTermKind.Exact, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[1].Terms[0].Kind);
    }

    [TestMethod]
    public void Parse_PrecisionTriggerInsideAWord_IsLiteralText()
    {
        // Only the FIRST character is ever read, so the '?' here is part of the text. '?' cannot occur in a
        // Windows file name, so such a term matches nothing -- the same fate the old "'" had inside a word.
        var pattern = FzfPattern.Parse("rep?ort");

        Assert.AreEqual(FzfTermKind.Fuzzy, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("rep?ort", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_LonePrecisionTrigger_AddsNoTerm()
    {
        // Same treatment a lone ':' gets: a stray trigger leaves the rest of the query untouched rather
        // than adding a term that can never match.
        Assert.IsTrue(FzfPattern.Parse("?").IsEmpty);
        Assert.AreEqual(FzfTermKind.Fuzzy, FzfPattern.Parse("? read").TermSets[0].Terms[0].Kind);
    }

    [TestMethod]
    public void Parse_PrecisionTriggerAfterAnExclusion_IsLiteralText()
    {
        // ':' is read first, so ":?temp" excludes the literal text "?temp". An exclusion is already pinned
        // to Exact, and a user who typed the colon first was writing a name, not an operator.
        var term = FzfPattern.Parse(":?temp").TermSets[0].Terms[0];

        Assert.IsTrue(term.Inverse);
        Assert.AreEqual(FzfTermKind.Exact, term.Kind);
        Assert.AreEqual("?temp", term.Text);
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
        //
        // KNOWN DEFECT (not this test's subject, so the assertion below records the current behaviour
        // rather than the intended one): the quote characters survive INTO the term text. A quoted phrase
        // therefore has to match apostrophes that no file name contains, so `'final report'` -- the form
        // documented under "Escaping Spaces & Quoted Phrases" in site/user-guide/search-syntax.md, and
        // listed as a grouping operator in Plan.md §2.2 -- returns nothing. Only the `final\ report` form
        // works, and only when FzfPattern.Parse sees it directly (the search box's QueryTokenScanner
        // consumes the escape first). Fixing it means stripping the delimiters where the phrase is folded,
        // teaching the merger about double quotes too, and keeping the scanner from eating `\ ` -- one
        // change across three places, which is why it is reported rather than pinned here.
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

    // IsEmpty means "no query here", which is what its callers act on -- NameSearch's drive gate returns
    // without searching, and FuzzyMatcher treats it as a non-match. A regex clause IS a query even though
    // it carries no term, so counting term sets alone made "/\.exe$/" search for nothing at all.
    [TestMethod]
    public void IsEmpty_RegexOnlyPattern_IsNotEmpty() => Assert.IsFalse(FzfPattern.Parse(@"/\.exe$/").IsEmpty);

    [TestMethod]
    public void IsEmpty_RegexAlongsideATerm_IsNotEmpty() => Assert.IsFalse(FzfPattern.Parse(@"lertaro /\.exe$/").IsEmpty);

    [TestMethod]
    public void IsEmpty_NoTermsAndNoRegex_IsEmpty()
    {
        Assert.IsTrue(FzfPattern.Parse("").IsEmpty);
        Assert.IsTrue(FzfPattern.Parse(":").IsEmpty);
    }

    private static void WithFuzzyDisabled(Action body)
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = false;
        try { body(); }
        finally { SearchContext.FuzzyMatchEnabled = previous; }
    }
}
