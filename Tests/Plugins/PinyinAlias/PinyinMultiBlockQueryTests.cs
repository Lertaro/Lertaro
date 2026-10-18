using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The reported ticket: a 184-character filename whose pinyin only matches block by block, from several
// unrelated parts of the name (防弹 ... 保护 ... 电脑玩物站长 ... 市集). Its readings are decided LIVE per
// candidate (nothing is baked -- see PinyinLiveSnapshotTests), which makes this class about the query
// side: whether a match is accepted, and what gets highlighted, when the syllables sit far apart. A
// token is one contiguous run, so blocks from different parts of the name need their own spaces.
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
    // The shapes the ticket names: the whole reading of each character, the initials of each character,
    // a mix of both, several such blocks at once, and pinyin sharing the query with literal text.
    [DataRow("fangdanbijifa")]
    [DataRow("jiandanshuchushi")]
    [DataRow("chuangyishiji")]
    [DataRow("diannaowanwuzhanzhang")]
    [DataRow("gcc")]
    [DataRow("cbcb")]
    [DataRow("cbcbjt")]
    [DataRow("fdan")]
    [DataRow("jijiagou")]
    [DataRow("xiaonxl")]
    [DataRow("chuangysji")]
    [DataRow("gxiao")]
    [DataRow("fdbjf gxiao")]
    [DataRow("fdbjf gcc")]
    [DataRow("fdan xiaonxl chuangysji")]
    [DataRow("diannao chu ban chuban df7686")]
    [DataRow("Esor xinliu cbcb archive")]
    [DataRow("9786267")]
    [DataRow("Anna Archive")]
    [DataRow("xinl gxiao jt fd 简单")]
    public void AnyBlockOrCombinationOfBlocks_FindsTheFile(string query)
    {
        using var fixture = new RealIndexFixture(Name);

        CollectionAssert.Contains(fixture.Search(query), Name, $"'{query}' must reach the file");
    }

    [TestMethod]
    [DataRow("fangdanbijifa")]
    [DataRow("fdbjf")]
    [DataRow("fdan")]
    [DataRow("fdbjf gxiao")]
    [DataRow("fdbjf gcc")]
    [DataRow("fdan xiaonxl chuangysji")]
    [DataRow("diannao chu ban chuban df7686")]
    [DataRow("Esor xinliu cbcb archive")]
    [DataRow("xinl gxiao jt fd 简单")]
    [DataRow("gcc")]
    [DataRow("jijiagou")]
    public void SameQuery_ReachesTheSameFileWithFuzzyMatchingOnAndOff(string query)
    {
        using var fixture = new RealIndexFixture(Name);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = false;
            var exact = fixture.Search(query).OrderBy(n => n, StringComparer.Ordinal).ToList();
            SearchContext.FuzzyMatchEnabled = true;
            var fuzzy = fixture.Search(query).OrderBy(n => n, StringComparer.Ordinal).ToList();

            CollectionAssert.Contains(exact, Name, $"'{query}' must reach the file with fuzzy matching off");
            CollectionAssert.AreEqual(exact, fuzzy, $"'{query}' must reach the same files with the setting either way");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
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
    [DataRow("fangdan bi ji fa")]
    public void MultiBlockQuery_DoesNotLightUnrelatedCharacters(string query)
    {
        using var fixture = new RealIndexFixture(Name);

        var lit = fixture.HighlightedIndices(Name, query);

        Assert.IsTrue(lit.Contains(0) && lit.Contains(1), "'fangdan' spells out 防弹's pinyin");
        foreach (var index in Unrelated)
            Assert.IsFalse(lit.Contains(index), $"position {index} ('{Name[index]}') is not part of '{query}'");
    }
}
