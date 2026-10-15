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
        Assert.AreEqual(0, readableStrings, "the enumerated aliases must have lost to the grid for this name");
    }

    [TestMethod]
    public void SmallReadingSpace_KeepsTheStringEncodingInstead()
    {
        // Every character monophonic, so the spelled-out form is two short strings and wins the size
        // comparison. (A name with even one polyphonic character enumerates its readings into a
        // '|'-joined cross product, which is where the grid starts to pay off.)
        var name = "高效笔记.pdf";
        using var fixture = new RealIndexFixture(name);

        var (_, readableStrings) = fixture.AliasCounts(name);

        Assert.IsTrue(readableStrings > 0, "a name with one reading per position spells out to a handful of bytes");
        CollectionAssert.Contains(fixture.Search("gxbj"), name);
    }
}
