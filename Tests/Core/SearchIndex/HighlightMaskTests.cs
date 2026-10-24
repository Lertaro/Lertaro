using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class HighlightMaskTests
{
    [TestMethod]
    public void Compute_EmptyText_ReturnsEmptyMask()
    {
        var mask = HighlightMask.Compute("", FzfPattern.Parse("read"));

        Assert.IsEmpty(mask);
    }

    [TestMethod]
    public void Compute_LiteralSubstring_MarksEveryOccurrence()
    {
        // MarkLiteralSpan finds every occurrence of a term, not just the first.
        var mask = HighlightMask.Compute("ababab", FzfPattern.Parse("ab"));

        CollectionAssert.AreEqual(new[] { true, true, true, true, true, true }, mask);
    }

    [TestMethod]
    public void Compute_ScatteredFuzzyMatch_MarksOnlyMatchedCharacters()
    {
        // "cwx" against "china_white_x" has no literal substring -- it falls through to the direct fuzzy
        // backtrace (FzfPositionMatcher), which must mark the three characters it actually matched and
        // nothing else. Asserting the exact mask is the point: a mask that marks every character would
        // still pass a "did it mark the matched ones" check, which is how this test used to be written
        // (against a text the pattern matched in full, so it could never tell the two apart).
        var mask = HighlightMask.Compute("china_white_x", FzfPattern.Parse("cwx"));

        CollectionAssert.AreEqual(
            new[] { true, false, false, false, false, false, true, false, false, false, false, false, true },
            mask);
    }

    // Regression coverage: Mark used to always highlight the OR set's FIRST term regardless of whether
    // it actually matched this candidate, mirroring FzfPattern.TryMatchSingle's per-set "best matching
    // term" semantics only in its own comment, not its code. A candidate that only matched via the
    // second or third OR term came back with an all-false mask -- no highlight at all -- even though the
    // real match algorithm matched it correctly via that later term.
    [TestMethod]
    public void Compute_OrQuery_HighlightsWhicheverTermActuallyMatchedTheCandidate()
    {
        var pattern = FzfPattern.Parse("123 | 456 | 789");

        var maskForFirstTerm = HighlightMask.Compute("123", pattern);
        var maskForSecondTerm = HighlightMask.Compute("456", pattern);
        var maskForThirdTerm = HighlightMask.Compute("789", pattern);

        Assert.IsTrue(Array.TrueForAll(maskForFirstTerm, m => m));
        Assert.IsTrue(Array.TrueForAll(maskForSecondTerm, m => m));
        Assert.IsTrue(Array.TrueForAll(maskForThirdTerm, m => m));
    }

    // When a candidate contains MORE THAN ONE of the OR set's terms (e.g. "我爱我家" contains both "我"
    // and "爱" from "我 | 爱 | 你"), every matching term highlights -- the union of "我"'s two
    // occurrences AND "爱"'s one occurrence, not just whichever term happens to be tried first.
    [TestMethod]
    public void Compute_OrQuery_CandidateContainsMultipleMatchingTerms_HighlightsTheUnionOfAllOfThem()
    {
        var pattern = FzfPattern.Parse("我 | 爱 | 你");

        var mask = HighlightMask.Compute("我爱我家", pattern);

        CollectionAssert.AreEqual(new[] { true, true, true, false }, mask);
    }

    [TestMethod]
    public void Compute_NoMatch_ReturnsAllFalseMask()
    {
        var mask = HighlightMask.Compute("readme", FzfPattern.Parse("xyz"));

        Assert.IsFalse(Array.Exists(mask, m => m));
    }

    // A "/.../" clause is a name-level clause: the display mask has to show the span it selected, or a
    // regex-only search renders as one long unmarked run and the user cannot tell why the row matched.
    [TestMethod]
    public void Compute_RegexClause_MarksTheSpanItSelected()
    {
        Assert.AreEqual(".txt", Marked("readme.txt", @"/\.txt$/"));
        Assert.AreEqual("2024", Marked("report 2024 final", "/\\d{4}/"));
    }

    [TestMethod]
    public void Compute_TermAndRegexClause_MarksBothRuns()
    {
        Assert.AreEqual("report|.docx", Marked("Q3 report 2024.docx", @"report /\.docx$/"));
    }

    [TestMethod]
    public void Compute_RegexClause_IsCaseInsensitiveUnlessTheClauseSaysOtherwise()
    {
        Assert.AreEqual("READ", Marked("READ.txt", "/read/"));
        Assert.AreEqual("read", Marked("Read read", "/(?-i)read/"));
    }

    [TestMethod]
    public void Compute_RegexClauseThatDoesNotMatch_MarksNothing()
    {
        Assert.AreEqual(string.Empty, Marked("readme", "/zzz/"));
    }

    // Paints the marked stretches as substrings, which reads better in a failure than a bool[].
    private static string Marked(string text, string query)
    {
        var mask = HighlightMask.Compute(text, FzfPattern.Parse(query));
        Assert.HasCount(text.Length, mask, "the mask must cover the text exactly");

        var parts = new List<string>();
        for (var i = 0; i < mask.Length;)
        {
            if (!mask[i]) { i++; continue; }
            var start = i;
            while (i < mask.Length && mask[i]) i++;
            parts.Add(text[start..i]);
        }
        return string.Join("|", parts);
    }

    [TestMethod]
    public void ComputeWeight_EmptyText_ReturnsZero() => Assert.AreEqual(0, HighlightMask.ComputeWeight("", FzfPattern.Parse("read")));

    [TestMethod]
    public void ComputeWeight_FullMatch_ReturnsOne() => Assert.AreEqual(1.0, HighlightMask.ComputeWeight("read", FzfPattern.Parse("read")));

    [TestMethod]
    public void ComputeWeight_NoMatch_ReturnsZero() => Assert.AreEqual(0, HighlightMask.ComputeWeight("readme", FzfPattern.Parse("xyz")));

    [TestMethod]
    public void ComputeWeight_ContiguousMatch_ScoresHigherThanScattered()
    {
        var contiguous = HighlightMask.ComputeWeight("abcdef", FzfPattern.Parse("abc"));
        var scattered = HighlightMask.ComputeWeight("axbxcx", FzfPattern.Parse("abc"));

        Assert.IsGreaterThan(scattered, contiguous);
    }

    // Weight is coverage*contiguity only. Left-side match priority is a SEPARATE, higher-ranked tier
    // reported alongside it (MatchRank.Start / HighlightMask.ComputeRank), precisely because coverage
    // alone structurally favours the shorter candidate: here "iwxfe.mp" (2/8) keeps the larger weight
    // even though its match starts a character later than "wxfef.doc" (2/9).
    [TestMethod]
    public void ComputeRank_WeightIgnoresPosition_AndStartReportsTheLeftmostIndex()
    {
        var atStart = HighlightMask.ComputeRank("wxfef.doc", FzfPattern.Parse("wx"));
        var oneIn = HighlightMask.ComputeRank("iwxfe.mp", FzfPattern.Parse("wx"));

        Assert.AreEqual(0, atStart.Start);
        Assert.AreEqual(1, oneIn.Start);
        Assert.IsGreaterThan(atStart.Weight, oneIn.Weight);
    }

    [TestMethod]
    public void ComputeRank_NoMatch_IsNotAMatch()
    {
        var rank = HighlightMask.ComputeRank("readme", FzfPattern.Parse("xyz"));

        Assert.IsFalse(rank.IsMatch);
        Assert.AreEqual(0, rank.Weight);
    }

    [TestMethod]
    public void ComputeRank_StartAtZero_KeepsThePlainCoverageValue() =>
        // 2 matched characters out of 4, fully contiguous, starting at index 0: 0.5 * 1.
        Assert.AreEqual(0.5, HighlightMask.ComputeRank("abcd", FzfPattern.Parse("ab")).Weight);
}
