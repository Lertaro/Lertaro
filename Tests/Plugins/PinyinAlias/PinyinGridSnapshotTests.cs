using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The integration the plugin's own tests could not reach: the REAL provider, baked by the real
// SnapshotWriter into a real snapshot, found by the real searcher. Under the live architecture the
// snapshot carries NOTHING for the readings -- every candidate re-walks its own readings at query
// time -- so what this class holds is that the zero-payload promise and the searches stand together.
[TestClass]
[DoNotParallelize]
public sealed class PinyinLiveSnapshotTests
{
    private static string LongPolyphonicName => new string('重', 40) + ".pdf";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    [TestMethod]
    public void LongPolyphonicName_IsFoundWithNoAliasesBaked()
    {
        var name = LongPolyphonicName;
        using var fixture = new RealIndexFixture(name);

        // "cz" asks for the second reading of one character and then the first reading of a later
        // one; the live matcher walks each character's whole reading set, so both are in hand.
        CollectionAssert.Contains(fixture.Search("cz"), name);

        var (hasAliases, readableStrings) = fixture.AliasCounts(name);

        Assert.IsFalse(hasAliases, "nothing about the readings is baked -- matching is live now");
        Assert.AreEqual(0, readableStrings);
    }

    [TestMethod]
    public void EveryReadingsWholeSyllableAndInitial_IsReachable()
    {
        using var fixture = new RealIndexFixture("创意市集.pdf");

        foreach (var query in new[] { "chuangyishiji", "chuangysji", "cysj" })
            CollectionAssert.Contains(fixture.Search(query), "创意市集.pdf", $"'{query}' must reach the file");
    }

    [TestMethod]
    public void MixedAlphabetTerm_StillReachesTheName()
    {
        // "电nao" mixes a native-script character with pinyin letters: the literal run has to match
        // the name and the letters a spelling of it (materialized live -- see AppendSpellings), which
        // is the one lane that needs an alias as text.
        var name = "电脑.pdf";
        using var fixture = new RealIndexFixture(name);

        CollectionAssert.Contains(fixture.Search("电nao"), name);
        Assert.IsTrue(FuzzyMatcher.IsMatch("电nao", name), "the public seam keeps the same tier");
    }

    [TestMethod]
    public void TwoTermSets_AgreeWithTheSeam()
    {
        // Each term is its own contiguous run; the row must be found by the index AND by the seam the
        // live tiers, the App's item gate and the ranking key read -- an index hit the seam denies is
        // a row those windows then order as a path fallback.
        const string Name = "屏幕截图滚动截图截屏录屏扫描标记ocr马赛克.txt";
        using var fixture = new RealIndexFixture(Name);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            foreach (var query in new[] { "pmjietu jietu", "pmjietu msk" })
            {
                SearchContext.FuzzyMatchEnabled = false;
                CollectionAssert.Contains(fixture.Search(query), Name, $"'{query}' must reach the file");

                Assert.IsTrue(FuzzyMatcher.IsMatch(query, Name), $"'{query}' must reach the file through the seam too");

                // An empty mask is NoMatch for the App's item gate, so a found row that lights nothing
                // is a row the window drops.
                Assert.IsTrue(fixture.HighlightedIndices(Name, query).Count > 0, $"'{query}' must light something");

                SearchContext.FuzzyMatchEnabled = true;
                CollectionAssert.AreEqual(new[] { Name }, fixture.Search(query).Take(1).ToArray(), $"'{query}' must answer the same with fuzzy matching on");
            }
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    [TestMethod]
    public void SpacedPinyin_ReusesTheAcceptedJoinedWitness()
    {
        const string Name = "搜索结果.txt";
        const string DeltaName = "搜索结果增量.txt";
        const string Joined = "sousuojieguo";
        var spacedVariants = new[] { "sousuo jieguo", "sou suo jie guo" };
        using var fixture = new RealIndexFixture(
            Name,
            "搜索结果备份.txt",
            "旧搜索结果.txt",
            "搜索结论.txt",
            "搜素结果.txt");
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            foreach (var fuzzy in new[] { false, true })
            {
                SearchContext.FuzzyMatchEnabled = fuzzy;
                var joinedResults = fixture.Search(Joined);
                var joinedQuery = FuzzyQuery.Parse(Joined);
                var joinedAccepted = joinedQuery.RankWithEvidence(Name, MatchEvidenceField.FileName);
                CollectionAssert.Contains(joinedResults, Name);

                foreach (var spaced in spacedVariants)
                {
                    var spacedResults = fixture.Search(spaced);
                    CollectionAssert.Contains(spacedResults, Name);
                    CollectionAssert.Contains(fixture.SearchWithAddedDelta(spaced, DeltaName), DeltaName);
                    Assert.IsTrue(Math.Abs(joinedResults.IndexOf(Name) - spacedResults.IndexOf(Name)) <= 1,
                        $"the controlled target rank for '{spaced}' may move by at most one place");
                    CollectionAssert.AreEqual(
                        fixture.HighlightedIndices(Name, Joined).ToArray(),
                        fixture.HighlightedIndices(Name, spaced).ToArray());

                    var spacedQuery = FuzzyQuery.Parse(spaced);
                    Assert.AreEqual(joinedQuery.Rank(Name), spacedQuery.Rank(Name));
                    var accepted = spacedQuery.RankWithEvidence(Name, MatchEvidenceField.FileName);
                    Assert.AreEqual(joinedAccepted.Evidence.Grade, accepted.Evidence.Grade);
                    Assert.AreEqual(MatchQueryVariant.PinyinJoined, accepted.Evidence.QueryVariant);
                    Assert.AreEqual(MatchEvidenceMode.PinyinFull, accepted.Evidence.Mode);
                    Assert.AreEqual(nameof(PinyinAliasProvider), accepted.Evidence.ProviderId);
                    Assert.AreEqual(0, accepted.Evidence.SpanStart);
                    Assert.AreEqual(4, accepted.Evidence.SpanLength);
                }
            }
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    [TestMethod]
    public void MultiTermContiguity_CountsEachTermAsOneRun()
    {
        // "cf" is literal and "wj" is the initials of 文件; they sit in two clean runs. The per-term
        // measure must not merge the gap between them into a broken single span.
        var desktop = FuzzyQuery.Parse("cf wj").Rank("cf 桌面文件");
        var ranch = FuzzyQuery.Parse("cf wj").Rank("牧场粪污技术交流.doc");

        Assert.IsTrue(desktop.IsMatch);
        Assert.IsTrue(ranch.IsMatch);
        Assert.AreEqual(1d, desktop.Contiguity);
        Assert.IsGreaterThan(0.5, desktop.Coverage, "precondition: the match explains more than half the name");
        Assert.IsLessThan(0, MatchRank.CompareQuality(desktop, ranch),
            "the two clean terms must beat the single pinyin run on coverage");
    }

    [TestMethod]
    public void JoinedBranch_DoesNotChangeOrdinaryMultiWordGrammar()
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            foreach (var fuzzy in new[] { false, true })
            {
                SearchContext.FuzzyMatchEnabled = fuzzy;
                foreach (var (query, text) in new[]
                {
                    ("visual studio", "Visual Studio"),
                    ("fast stone", "FastStone"),
                    ("jie guo sou suo", "搜索结果"),
                    ("sou suo jie guo", "搜索-结果"),
                })
                {
                    var accepted = FuzzyQuery.Parse(query)
                        .RankWithEvidence(text, MatchEvidenceField.DisplayTitle);
                    Assert.IsTrue(accepted.Rank.IsMatch, $"'{query}' must retain its ordinary match");
                    Assert.AreEqual(MatchQueryVariant.Original, accepted.Evidence.QueryVariant);
                }

                Assert.IsFalse(FuzzyMatcher.IsMatch("sousuojieguo", "搜索-结果"),
                    "a joined pinyin token must not cross a candidate delimiter");
            }
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    [TestMethod]
    public void TwoTerms_AreIndependentAndOrderFree()
    {
        // "pmjt" (屏幕截图) and "bj" (标记) are separate terms: each is one contiguous run, the two do
        // not constrain each other's position, and either typed order answers the same.
        const string Name = "屏幕截图滚动截图截屏录屏扫描标记ocr马赛克翻译.txt";
        using var fixture = new RealIndexFixture(Name);

        foreach (var query in new[] { "pmjt bj", "bj pmjt" })
        {
            Assert.IsTrue(fixture.Search(query).Contains(Name), $"'{query}' must reach the file");
            Assert.IsTrue(FuzzyMatcher.IsMatch(query, Name), $"'{query}' must reach it through the seam too");
        }
    }
}
