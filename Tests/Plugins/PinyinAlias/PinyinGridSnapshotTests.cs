using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The integration the plugin's own tests could not reach: the REAL provider, baked by the real
// SnapshotWriter into a real snapshot, found by the real searcher. Under the live architecture the
// snapshot carries NOTHING for the readings -- every candidate re-walks its own readings at query
// time -- so what this class holds is that the zero-payload promise and the searches stand together.
[TestClass]
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
