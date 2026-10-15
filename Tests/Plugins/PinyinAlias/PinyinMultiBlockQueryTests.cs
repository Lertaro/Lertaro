using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The reported ticket: a 184-character filename whose pinyin only matches block by block, from several
// unrelated parts of the name (防弹 ... 保护 ... 电脑玩物站长 ... 市集). The aliases for it are two
// STRINGS, not a grid -- its full pinyin overruns the combination path, so the single-reading fallback
// produces them -- which makes this class about the query side: whether a match is accepted, and what
// gets highlighted, when the syllables sit far apart.
[TestClass]
public sealed class PinyinMultiBlockQueryTests
{
    private const string Name =
        "防弹笔记法-简单输出式笔记架构,保护高产出、高效能心流 -- 电脑玩物站长Esor -- View zhi chang li, 2AB964, Chu ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive";

    // Positions that a previous, too-loose highlight painted for "fangdan": they are the greedy
    // subsequence of the flattened initials alias, not anything the user typed.
    private static readonly int[] Unrelated = { 14, 17, 31, 32 };

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    [TestMethod]
    [DataRow("fdbjf")]
    [DataRow("gao")]
    [DataRow("fdbjf gao")]
    [DataRow("fangdan")]
    [DataRow("baohu")]
    [DataRow("bao hu")]
    [DataRow("nx")]
    [DataRow("sj")]
    [DataRow("baohu nx")]
    [DataRow("dnwwzz sj")]
    [DataRow("fangdan baohu nx dnwwzz sj")]
    public void AnyBlockOrCombinationOfBlocks_FindsTheFile(string query)
    {
        using var fixture = new RealIndexFixture(Name);

        CollectionAssert.Contains(fixture.Search(query), Name, $"'{query}' must reach the file");
    }

    [TestMethod]
    public void AbbreviatedQuery_HighlightsTheCharacterItSpellsOut()
    {
        using var fixture = new RealIndexFixture(Name);

        // "bi" is 笔's pinyin; the name's ASCII tail also contains b…i in "bei", so both positions are
        // legitimate. What must not happen is the alias tier being skipped in favour of the name's own
        // scattered pass, which painted "chu ban" and "tai bei shi" instead.
        var lit = fixture.HighlightedIndices(Name, "bi");

        CollectionAssert.Contains(lit, 2);
        Assert.IsTrue(lit.TrueForAll(index => index == 2 || Name[index] is 'b' or 'i'),
            $"unexpected positions: {string.Join(",", lit.Select(index => $"{index}:{Name[index]}"))}");
    }

    [TestMethod]
    [DataRow("fangdan")]
    [DataRow("fangdan bi j f")]
    public void MultiBlockQuery_DoesNotLightUnrelatedCharacters(string query)
    {
        using var fixture = new RealIndexFixture(Name);

        var lit = fixture.HighlightedIndices(Name, query);

        Assert.IsTrue(lit.Contains(0) && lit.Contains(1), "'fangdan' spells out 防弹's pinyin");
        foreach (var index in Unrelated)
            Assert.IsFalse(lit.Contains(index), $"position {index} ('{Name[index]}') is not part of '{query}'");
    }
}
