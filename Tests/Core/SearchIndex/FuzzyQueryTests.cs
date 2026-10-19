using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex;

// FuzzyQuery must be a pure optimization: parsing once and reusing must give byte-for-byte the same
// verdict as the string APIs that parse on every call. If these ever diverge, the App's ranking would
// silently disagree with Core's own file search for the same query.
[TestClass]
public sealed class FuzzyQueryTests
{
    private static readonly string[] Texts =
    {
        "wxfef.doc", "iwxfe.mp", "Report-Final.docx", "readme.md", "a_b_c.txt", "中文报告.txt",
    };

    [TestMethod]
    public void Rank_MatchesTheStringApiForEveryText()
    {
        foreach (var query in new[] { "wx", "report", "rdm", "a" })
        {
            var fuzzy = FuzzyQuery.Parse(query);
            foreach (var text in Texts)
            {
                var expected = FuzzyMatcher.ComputeMatchRank(text, query);
                var actual = fuzzy.Rank(text);

                Assert.AreEqual(expected, actual, $"query='{query}' text='{text}'");
            }
        }
    }

    [TestMethod]
    public void IsMatch_MatchesTheStringApiForEveryText()
    {
        foreach (var query in new[] { "wx", "report", "rdm", "zzz" })
        {
            var fuzzy = FuzzyQuery.Parse(query);
            foreach (var text in Texts)
                Assert.AreEqual(FuzzyMatcher.IsMatch(query, text), fuzzy.IsMatch(text), $"query='{query}' text='{text}'");
        }
    }

    [TestMethod]
    public void BestMatch_MatchesTheStringApi()
    {
        var fuzzy = FuzzyQuery.Parse("read");

        var expected = FuzzyMatcher.ComputeBestMatch("read", "r_e_a_d_noisy", new[] { "unrelated", "read.txt" });
        var actual = fuzzy.BestMatch("r_e_a_d_noisy", new[] { "unrelated", "read.txt" });

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Parse_EmptyOrNull_IsEmptyAndMatchesNothing()
    {
        Assert.IsTrue(FuzzyQuery.Parse("").IsEmpty);
        Assert.IsTrue(FuzzyQuery.Parse(null).IsEmpty);
        Assert.IsFalse(FuzzyQuery.Parse("a").IsEmpty);

        var empty = FuzzyQuery.Parse("");
        Assert.IsFalse(empty.IsMatch("anything"));
        Assert.IsFalse(empty.Rank("anything").IsMatch);
        Assert.IsFalse(empty.BestMatch("anything").IsMatch);
        Assert.IsEmpty(empty.HighlightMask("anything"));
    }

    // The quick window calls this for rows whose own name does not match the query, and the full window's row
    // key reaches the same rank through SearchResultRelevance.Apply. Pinned against that value rather than
    // against literals, because "one query, one file order" is the whole point of it.
    [TestMethod]
    public void FallbackRank_IsTheRankTheFullWindowsRowKeyGivesSuchARow()
    {
        const string path = @"C:\Docs\annual-report-2024.pdf";

        var expected = MatchRank.NoNameFallback(HighlightMask.ComputeWeight(path, FzfPattern.Parse("report")));

        Assert.AreEqual(expected, FuzzyQuery.Parse("report").FallbackRank(path));
        Assert.IsTrue(FuzzyQuery.Parse("report").FallbackRank(path).Coverage > 0, "the path still explains part of the query");
    }

    [TestMethod]
    public void FallbackRank_WithoutAQueryOrPath_IsNoMatch()
    {
        Assert.IsFalse(FuzzyQuery.Parse("").FallbackRank(@"C:\Docs\a.txt").IsMatch);
        Assert.IsFalse(FuzzyQuery.Parse("report").FallbackRank(null).IsMatch);
        Assert.IsFalse(FuzzyQuery.Parse("report").FallbackRank(string.Empty).IsMatch);
    }

    [TestMethod]
    public void Text_KeepsTheOriginalQuery()
    {
        Assert.AreEqual("Report", FuzzyQuery.Parse("Report").Text);
        Assert.AreEqual(string.Empty, FuzzyQuery.Parse("").Text);
    }
}
