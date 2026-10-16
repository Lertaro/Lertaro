using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The ticket this exists for: "gxiao" -- 高's initial followed by 效's WHOLE reading -- is a pinyin
// abbreviation that appears as an unbroken run in no alias of any name, so with fuzzy matching off it
// reached almost none of the names it spells out (measured over a 1201-name corpus: 2 of 665, against 504
// with fuzzy on). The engine now reads an alias both ways while keeping the alignment rule, so the same
// query reaches the same name in both modes -- and a splice still does not.
//
// The compound query below pins the other half: its terms are satisfied by DIFFERENT spellings of the name --
// "简单" only by the name itself, "xinl"/"gxiao" only by the full-pinyin alias, "jt"/"fd" straight out of the
// initials alias. Requiring one spelling to carry the whole query is what made it unreachable before, in
// fuzzy mode as well.
[TestClass]
public sealed class PinyinExactModeTests
{
    private const string Name =
        "防弹笔记法-简单输出式笔记架构,保护高产出、高效能心流 -- 电脑玩物站长Esor -- View zhi chang li, 2AB964, Chu ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive";

    private const string Certificate = "管理用户证书.txt";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    private static List<string> Search(string query, bool fuzzy, params string[] names)
    {
        using var fixture = new RealIndexFixture(names);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = fuzzy;
            return fixture.Search(query);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    [TestMethod]
    // Every one of these mixes an initial with a whole reading, so none of them is a contiguous run of any
    // alias a name has.
    [DataRow("gxiao")]
    [DataRow("xinl gxiao")]
    [DataRow("fdbjf gao")]
    [DataRow("fangdan baohu nx dnwwzz sj")]
    [DataRow("xinl gxiao jt fd 简单")]
    public void MixedAbbreviation_ReachesItsNameInExactModeToo(string query)
    {
        CollectionAssert.Contains(Search(query, fuzzy: false, Name), Name, $"'{query}' must reach the file with fuzzy matching off");
        CollectionAssert.Contains(Search(query, fuzzy: true, Name), Name, $"'{query}' must reach the file with fuzzy matching on");
    }

    // A pinyin match may not BEGIN inside one of a name's readings, in EITHER mode: "ex" is not an
    // abbreviation of 学习's xue+xi but the tail of one syllable glued to the head of the next, and "huchu"
    // is the middle of 出's "chu" twice over. Fuzzy matching licenses a loose match against the candidate's
    // own text, never a word that no spelling of the name actually starts with.
    [TestMethod]
    public void CrossSyllableSplice_StaysOutOfBothModes()
    {
        string[] names = ["学习笔记.txt", "人行道.pdf"];

        Assert.IsEmpty(Search("ex", fuzzy: false, names), "'ex' spliced out of xue+xi is not what the user typed");
        Assert.IsEmpty(Search("ex", fuzzy: true, names), "and fuzzy matching does not license it either");
    }

    [TestMethod]
    public void MidReadingStart_StaysOutOfBothModes()
    {
        // 简单输出式: "huchu" opens inside 出's "chu" (输's "shu" is the other 'h' it can hide in), so no
        // spelling of this name starts where that match starts. "jdsc" / "jiandansc" / "jdshuc" all open on
        // 简's "jian" and stay reachable.
        string[] names = ["简单输出式笔记架构.txt"];

        Assert.IsEmpty(Search("huchu", fuzzy: false, names));
        Assert.IsEmpty(Search("huchu", fuzzy: true, names));
        foreach (var query in new[] { "jdsc", "jiandansc", "jdshuc", "schu", "jiandan schu ji jgou jian" })
            CollectionAssert.Contains(Search(query, fuzzy: false, names), names[0], $"'{query}' opens on a reading");
    }

    // The setting must not change how PINYIN reads a name: same query, same names through the alias tiers,
    // both modes. What still differs by mode is the candidate's own text (exact wants a run in the name,
    // fuzzy a scattered subsequence of it), which is exactly what the setting means -- so the queries here
    // are ones whose letters can only be satisfied by pinyin, never by a scatter across the name's own ASCII.
    [TestMethod]
    public void BothModes_ReachTheSameNamesForPinyinQueries()
    {
        string[] names = [Name, "简单输出式笔记架构.txt", Certificate];
        foreach (var query in new[]
                 {
                     "jdsc", "jiandansc", "jdshuc", "jiandan schu ji jgou jian", "huchu", "gxiao", "xinl",
                     "fdbjf", "fd bji", "jt", "gao", "cbcbjt", "zcl", "ex", "zhengsh", "piji",
                 })
        {
            CollectionAssert.AreEqual(
                Search(query, fuzzy: false, names).OrderBy(n => n).ToList(),
                Search(query, fuzzy: true, names).OrderBy(n => n).ToList(),
                $"'{query}' must reach exactly the same names with fuzzy off and on");
        }
    }

    [TestMethod]
    public void ExactMode_KeepsReachingWholeReadingsAndHalfTypedTails()
    {
        // Loose reading or not, exact mode did not turn into a subsequence search: a whole reading is still
        // reached from its own syllable boundary, and a half-typed trailing reading still completes.
        Assert.IsNotEmpty(Search("zhengsh", fuzzy: false, Certificate), "'zhengsh' is a run of 证书's reading");
        Assert.IsNotEmpty(Search("gsh", fuzzy: false, Certificate), "'gsh' is 管's initial plus the half-typed start of 书");

        // "ngsh" opens inside no reading at all -- no reading of this name starts with "n" -- and the name
        // itself carries no letters to scatter it across, so the rule refuses it outright, in both modes.
        Assert.IsEmpty(Search("ngsh", fuzzy: false, Certificate));
        Assert.IsEmpty(Search("ngsh", fuzzy: true, Certificate));
    }
}
