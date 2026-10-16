using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The integration the plugin's own tests could not reach: the REAL provider, baked by the real
// SnapshotWriter into a real snapshot, found by the real searcher. Everything else in this project
// exercises the plugin in isolation.
//
// The name is the shape the original ticket was about -- 40 polyphonic characters, whose spelled-out
// alias set is the cross product of its readings and therefore only ever partially enumerated. The row
// must be findable by a reading pattern the enumerated variants do not contain.
[TestClass]
public sealed class PinyinGridSnapshotTests
{
    private static string LongPolyphonicName => new string('重', 40) + ".pdf";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    [TestMethod]
    public void LongPolyphonicName_IsFoundThroughItsGridWithNoStringAliasToMatch()
    {
        var name = LongPolyphonicName;
        using var fixture = new RealIndexFixture(name);

        // "cz" asks for the SECOND reading of one character and then the first reading of a later one.
        // The enumerated variants all put the first reading in table order first, so no baked string can
        // supply that order; the grid can, because it holds every reading at every position.
        CollectionAssert.Contains(fixture.Search("cz"), name);
    }

    [TestMethod]
    public void LongPolyphonicName_BakesTheGridAndNoStringAliasesFromThePinyinProvider()
    {
        var name = LongPolyphonicName;
        using var fixture = new RealIndexFixture(name);

        // Proves the hit above came from the grid: the pinyin provider contributes no readable string
        // alias for this name at all, only the grid entry (which GetAliases deliberately skips).
        var (hasAliases, readableStrings) = fixture.AliasCounts(name);

        Assert.IsTrue(hasAliases, "the name must carry at least the grid entry");
        Assert.AreEqual(0, readableStrings, "the grid is the only form this provider's readings are stored in");
    }

    [TestMethod]
    public void SmallReadingSpace_TakesTheGridToo()
    {
        // The grid is how this provider's readings are stored, whatever the name's size: the spelled-out
        // form is the same space enumerated and capped, and a name can lose a reading to either cap
        // without being long or heavily polyphonic. (Measured cost: ~1.4x the spelled-out bytes here.)
        var name = "高效笔记.pdf";
        using var fixture = new RealIndexFixture(name);

        var (hasAliases, readableStrings) = fixture.AliasCounts(name);

        Assert.IsTrue(hasAliases);
        Assert.AreEqual(0, readableStrings, "nothing of this provider's is a readable string alias");
        CollectionAssert.Contains(fixture.Search("gxbj"), name);
    }

    [TestMethod]
    public void EveryReading_IsReachableThroughTheGrid()
    {
        // 市 reads "fu" and "shi", and "fu" comes first -- so a baked single-reading or enumerated alias
        // spells 创意市集 as "chuang|yi|fu|ji" and no query for what the characters actually say reaches
        // it. The grid holds both readings, and the rule (a piece is a whole reading or its initial) turns
        // them into the spellings a user types.
        using var fixture = new RealIndexFixture("创意市集.pdf");

        foreach (var query in new[] { "chuangyishiji", "chuangysji", "chuangyifuji", "cysj" })
            CollectionAssert.Contains(fixture.Search(query), "创意市集.pdf", $"'{query}' must reach the file");
    }

    [TestMethod]
    public void MixedAlphabetTerm_StillReachesTheName()
    {
        // "电nao" mixes a native-script character with pinyin letters: the literal run has to match the
        // name and the letters the spelling, and that lane reads an alias as TEXT (it maps the positions it
        // took back onto the source). A grid stores readings, so it is spelled out for this one lane --
        // without that, a name whose aliases are a grid loses this query shape entirely.
        var name = "电脑.pdf";
        using var fixture = new RealIndexFixture(name);

        CollectionAssert.Contains(fixture.Search("电nao"), name);
        Assert.IsTrue(FuzzyMatcher.IsMatch("电nao", name), "the public seam keeps the same tier");
    }
}
