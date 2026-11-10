using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The regex prefilter. A "/.../" clause cannot be mask-tested, so without the literal its extractor
// pulls out, the regex engine runs against every indexed name. These pin the wiring itself: the literal has
// to reach RequiredMask, and a clause with no literal must leave the prefilter exactly as it was rather
// than switching it off or narrowing it to nothing.
[TestClass]
public sealed class RegexPrefilterTests
{
    [TestMethod]
    [DataRow(@"/^Ａ$/", "Ａ")]
    [DataRow(@"/^\uFF21$/", "Ａ")]
    [DataRow(@"A /^Ａ$/", "Ａ")]
    [DataRow(@"/e\u0301/", "e\u0301")]
    public void Search_RegexReadsOriginalCharacters(string query, string expected)
    {
        using var fixture = LiveIndexFixture.Build("T", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Ａ", FileRecordFlags.None),
            new FileRecord(3, 1, "A", FileRecordFlags.None),
            new FileRecord(4, 1, "e\u0301", FileRecordFlags.None),
            new FileRecord(5, 1, "é", FileRecordFlags.None)
        });
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 20, results.Add, CancellationToken.None);
        Assert.AreEqual(expected, Assert.ContainsSingle(results).Name);
    }
    [TestMethod]
    [DataRow(@"/^\uFF21$/")]
    [DataRow(@"T:\ /^\uFF21$/")]
    [DataRow(@"T:\ A /^Ａ$/")]
    public void Search_DeltaAndPath_KeepOriginalRegexCharacters(string query)
    {
        using var fixture = LiveIndexFixture.Build("T", [LiveIndexFixture.Root()]);
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(2, 1, "Ａ", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Upsert(3, 1, "A", FileRecordFlags.None, 0, 0, 0, 0);
        });
        var rows = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, rows.Add, CancellationToken.None);
        Assert.AreEqual("Ａ", Assert.ContainsSingle(rows).Name);
    }

    [TestMethod]
    [DataRow(@"/^\u0061$/")]
    [DataRow(@"/^\x61$/")]
    [DataRow(@"/^\141$/")]
    [DataRow(@"/^a{00,2}b$/")]
    public void Search_AdvancedRegexSyntax_DoesNotLoseMatchingIndexedNames(string query)
    {
        var name = query.Contains("{00", StringComparison.Ordinal) ? "b" : "a";
        using var fixture = LiveIndexFixture.Build("T", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, name, FileRecordFlags.None)
        });
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, results.Add, CancellationToken.None);

        Assert.AreEqual(name, Assert.ContainsSingle(results).Name);
    }

    [TestMethod]
    public void BuildContext_RegexWithALiteral_AddsTheLiteralsBitsToTheMask()
    {
        var pattern = FzfPattern.Parse(@"/^report.*\.md$/");

        var ctx = SearchMatcher.BuildContext(pattern);

        Assert.AreEqual("report", pattern.RequiredRegexLiteral);
        Assert.IsTrue(ctx.CanFilter);
        Assert.AreEqual(FzfAlgorithm.GetCharMask("report"), ctx.RequiredMask & FzfAlgorithm.GetCharMask("report"));
    }

    [TestMethod]
    public void BuildContext_RegexLiteralAndTerm_MasksAreCombined()
    {
        // Both conditions are on the same text, so the mask is the union of their requirements -- the
        // candidate must contain the term's characters AND the clause's.
        var pattern = FzfPattern.Parse(@"readme /\.md$/");

        var ctx = SearchMatcher.BuildContext(pattern);

        var expected = FzfAlgorithm.GetCharMask("readme") | FzfAlgorithm.GetCharMask(".md");
        Assert.AreEqual(expected, ctx.RequiredMask);
    }

    [TestMethod]
    public void BuildContext_RegexWithoutALiteral_LeavesTheMaskUntouched()
    {
        // An alternation yields no literal, so there is nothing to demand -- and crucially this must not
        // become a "filter everything out" mask: a regex-only query with no literal reports no filtering
        // rather than an unsatisfiable one.
        var pattern = FzfPattern.Parse(@"/^(ogg|mp3)$/");

        var ctx = SearchMatcher.BuildContext(pattern);

        Assert.AreEqual(string.Empty, pattern.RequiredRegexLiteral);
        Assert.AreEqual(0UL, ctx.RequiredMask);
        Assert.IsFalse(ctx.CanFilter);
    }

    [TestMethod]
    public void BuildContext_PlainTermOnly_IsUnaffectedByTheRegexPath()
    {
        var pattern = FzfPattern.Parse("readme");

        var ctx = SearchMatcher.BuildContext(pattern);

        Assert.AreEqual(FzfAlgorithm.GetCharMask("readme"), ctx.RequiredMask);
    }
}
