using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

// Some of these flip SearchContext.DefaultFuzzyMatchEnabled, which is one process-wide field rather
// than a per-flow one -- while this assembly runs test METHODS in parallel. Anything that parses a
// query during that window reads whichever value it happens to see, so a handful of unrelated tests
// failed at random and never the same ones twice. MSTest runs non-parallelizable tests after the
// parallel batch, so this keeps the flip from overlapping anything.
//
// Reproduced before fixing, by holding the flip open for three seconds: four unrelated tests failed in
// one run, none in the next. AliasHighlightTests in the App project carries this attribute for exactly
// the same reason.
[TestClass]
[DoNotParallelize]
public sealed class FzfPatternTests
{
    [TestMethod]
    public void Parse_EmptyQuery_IsEmpty() => Assert.IsTrue(FzfPattern.Parse("").IsEmpty);

    [TestMethod]
    public void Parse_DriveLetterTerm_ExtractsTargetDriveAndDropsItFromTerms()
    {
        var pattern = FzfPattern.Parse("c: readme");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
    }

    // The space after the drive is optional. It used not to be: the drive test matched on the first two
    // characters and then dropped the WHOLE token, so "c:readme" searched drive C for nothing at all
    // while "c: readme" searched it for readme -- reported by a user who could see the two behaved
    // differently but had no way to tell why.
    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_KeepsTheRestAsATerm()
    {
        var pattern = FzfPattern.Parse("c:readme");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterWithAndWithoutASpace_AgreeExactly()
    {
        var spaced = FzfPattern.Parse("c: readme report");
        var joined = FzfPattern.Parse("c:readme report");

        Assert.AreEqual(spaced.TargetDrive, joined.TargetDrive);
        Assert.HasCount(spaced.TermSets.Length, joined.TermSets);
        for (var i = 0; i < spaced.TermSets.Length; i++)
            Assert.AreEqual(spaced.TermSets[i].Terms[0].Text, joined.TermSets[i].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterWithNoSpace_LeavesLaterTermsAlone()
    {
        var pattern = FzfPattern.Parse("c:readme report");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.HasCount(2, pattern.TermSets);
        Assert.AreEqual("readme", pattern.TermSets[0].Terms[0].Text);
        Assert.AreEqual("report", pattern.TermSets[1].Terms[0].Text);
    }

    [TestMethod]
    public void Parse_DriveLetterAlone_HasNoTerms()
    {
        var pattern = FzfPattern.Parse("c:");

        Assert.AreEqual("c", pattern.TargetDrive);
        Assert.IsEmpty(pattern.TermSets);
    }

    [TestMethod]
    public void Parse_LastDriveTokenWins()
    {
        var pattern = FzfPattern.Parse("c: d: readme");

        Assert.AreEqual("d", pattern.TargetDrive);
        Assert.HasCount(1, pattern.TermSets);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_MatchesSubsequence()
    {
        var pattern = FzfPattern.Parse("rdm");

        var matched = pattern.TryMatch("readme.md", out var result, FzfScoringScheme.Default);

        Assert.IsTrue(matched);
        Assert.IsTrue(result.ValidOffsetFound);
    }

    [TestMethod]
    public void TryMatch_PlainFuzzyTerm_FailsWhenSubsequenceAbsent()
    {
        var pattern = FzfPattern.Parse("xyz");

        var matched = pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default);

        Assert.IsFalse(matched);
    }

    [TestMethod]
    public void TryMatch_MultipleTerms_RequiresEveryTermToMatch()
    {
        var pattern = FzfPattern.Parse("read md");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_InverseTerm_RejectsTextContainingIt()
    {
        var pattern = FzfPattern.Parse("read !md");

        Assert.IsTrue(pattern.TryMatch("readme.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_PrefixTerm_OnlyMatchesAtStart()
    {
        var pattern = FzfPattern.Parse("^read");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("unread.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_SuffixTerm_OnlyMatchesAtEnd()
    {
        var pattern = FzfPattern.Parse("md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("md5sum.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_EqualTerm_RequiresExactWholeTextMatch()
    {
        var pattern = FzfPattern.Parse("^readme.md$");

        Assert.IsTrue(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md.bak", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_ExactBoundaryTerm_RequiresWholeSegmentMatch()
    {
        var pattern = FzfPattern.Parse("'read'");

        // "read" is its own dot-delimited segment in "my.read.txt" -- a boundary on both sides.
        Assert.IsTrue(pattern.TryMatch("my.read.txt", out _, FzfScoringScheme.Default));
        // In "readme.md" the match would end mid-word (right before "me"), which is not a boundary.
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
        // Not even a contiguous substring here.
        Assert.IsFalse(pattern.TryMatch("r-e-a-d.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_MixedCaseTerm_IsCaseSensitive()
    {
        var pattern = FzfPattern.Parse("README");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("readme.md", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_LowercaseTerm_IsCaseInsensitive()
    {
        var pattern = FzfPattern.Parse("readme");

        Assert.IsTrue(pattern.TryMatch("README.md", out _, FzfScoringScheme.Default));
    }

    // Reachable through this API only, never from the search box: a backslash anywhere in a query
    // makes SearchQueryParser classify it as path mode, which routes to PathSearch before
    // FzfPattern.Parse is ever called. Quoting ("'my file'") is the only search-box route to a term
    // containing a space.
    [TestMethod]
    public void TryMatch_EscapedSpace_IsTreatedAsLiteralSpaceInOneTerm()
    {
        var pattern = FzfPattern.Parse(@"my\ file");

        Assert.IsTrue(pattern.TryMatch("my file.txt", out _, FzfScoringScheme.Default));
        Assert.IsFalse(pattern.TryMatch("myfile.txt", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_MatchesIfEitherSegmentMatches()
    {
        var pattern = FzfPattern.Parse("he");

        // "he" and "hu" are alternate readings of the same alias, joined with '|' at the text side.
        Assert.IsTrue(pattern.TryMatch("he|hu|huo", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_BarSeparatedSegments_TermsFromDifferentSegmentsDoNotCombine()
    {
        var pattern = FzfPattern.Parse("ab cd");

        // "ab" only appears in the first segment and "cd" only in the second -- a match must find
        // both terms within the SAME segment, not scattered across the whole joined string.
        Assert.IsFalse(pattern.TryMatch("ab|cd", out _, FzfScoringScheme.Default));
        Assert.IsTrue(pattern.TryMatch("abcd|xy", out _, FzfScoringScheme.Default));
    }

    [TestMethod]
    public void TryMatch_QuotedPhraseContainingSpaces_IsOneBoundaryTerm()
    {
        var pattern = FzfPattern.Parse("'cad acb'");

        Assert.HasCount(1, pattern.TermSets);
        Assert.HasCount(1, pattern.TermSets[0].Terms);
        Assert.AreEqual(FzfTermKind.ExactBoundary, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("cad acb", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
        // The space is literal, so a differently delimited name is not a match.
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

        // Neither token starts with a quote, so this stays two ordinary fuzzy terms.
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

        // One set holding two alternatives (an OR), not a single phrase swallowing the '|'.
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

    // Carries the same API-level-only caveat as TryMatch_EscapedSpace_IsTreatedAsLiteralSpaceInOneTerm
    // (a backslash sends the query to path mode first). Kept as a regression guard so the quoted-phrase
    // merge cannot silently break the older escape form, not as a search-box syntax anyone can type.
    [TestMethod]
    public void TryMatch_EscapedSpaceInsideQuotedPhrase_StillParsesAsOneTerm()
    {
        var pattern = FzfPattern.Parse(@"'cad\ acb'");

        Assert.AreEqual(FzfTermKind.ExactBoundary, pattern.TermSets[0].Terms[0].Kind);
        Assert.AreEqual("cad acb", pattern.TermSets[0].Terms[0].Text);
        Assert.IsTrue(pattern.TryMatch("cad acb.txt", out _, FzfScoringScheme.Default));
    }

    // SearchContext.FuzzyMatchEnabled is an AsyncLocal, so each case restores it rather than leaving
    // the flipped value to leak into whatever test the runner schedules next on this context.
    private static void WithFuzzyDisabled(Action body)
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        SearchContext.FuzzyMatchEnabled = false;
        try { body(); }
        finally { SearchContext.FuzzyMatchEnabled = previous; }
    }

    // DefaultFuzzyMatchEnabled is process-wide, unlike the AsyncLocal the other cases flip, so these
    // two have to leave the parallel phase entirely -- otherwise they change what every concurrently
    // running pattern parse in this assembly sees.
    [TestMethod]
    [DoNotParallelize]
    public void Parse_ProcessDefaultDisabled_AppliesWithoutAnyPerRequestValue()
    {
        // The regression this guards: plugin catalog items, favorites and highlighting all parse
        // patterns on call paths the search pipeline's AsyncLocal never reaches, so they have to
        // follow the process-wide default instead of silently staying fuzzy.
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
            // A search request that explicitly asks for fuzzy must win over a disabled process default.
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
        // The whole point: each term must now be contiguous, so scattered characters no longer match.
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
        // "'" flips exactness rather than setting it, so it stays the per-term escape hatch in
        // both modes -- matching fzf's own behavior under --exact.
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
        // A set's terms are alternatives -- an OR branch here, an alias provider's other spelling of
        // the same term elsewhere -- so only one of them can be what the user typed. Summing them
        // inflated the length that IsAcceptableAliasMatch scales its thresholds against, which
        // rejected real matches purely because a term had several readings.
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
}
