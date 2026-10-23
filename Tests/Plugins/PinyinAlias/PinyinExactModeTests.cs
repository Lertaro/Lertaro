using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The compound queries the live matcher must carry: several space-separated terms, each its own
// contiguous run, terms independent of one another (no shared spelling, no order), and the two
// matching modes answering the same thing for anything pinyin.
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
    // Each term mixes an initial with a whole reading somewhere in the name; the terms live in
    // different parts of it and are matched independently.
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

    // A pinyin match must BEGIN where a reading begins and stay on adjacent characters: "ex" would
    // need an 'e'-opening reading (neither name has one -- 学 opens x-u-e) and "huchu" an 'h' the
    // name does not begin anywhere. Both are refused in EITHER mode.
    [TestMethod]
    public void CrossSyllableSplice_StaysOutOfBothModes()
    {
        string[] names = ["学习笔记.txt", "人行道.pdf"];

        Assert.IsEmpty(Search("ex", fuzzy: false, names), "'ex' cannot open on any reading of these names");
        Assert.IsEmpty(Search("ex", fuzzy: true, names), "and fuzzy matching does not license it either");
    }

    // #440's compact positive: a literal exact term cannot cross the hyphens, but the shared alias gate
    // still accepts the 11-character occurrence. This pins the real provider path the user sees.
    [TestMethod]
    public void Reported440_TightAliasPositiveCandidatesWithRealProvider()
    {
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = false;
            Assert.IsTrue(FuzzyMatcher.IsMatch("zbl", "著 z-aa-b-aa-l.epub"));
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    // The real pipeline: snapshot -> LiveIndex -> searcher. This is the candidate layer #440's absence
    // claim is about, so the loose row must not appear at all while the tight row still does.
    [TestMethod]
    public void Reported440_RealIndexCandidateLayerKeepsOnlyTheTightAlias()
    {
        const string loose = "著 z-aaa-b-aaaaa-l.epub";
        const string tight = "著 z-aa-b-aa-l.epub";
        using var fixture = new RealIndexFixture(loose, tight);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = false;
            var results = fixture.Search("zbl");
            CollectionAssert.Contains(results, tight, "the 11-character tight occurrence must be a candidate");
            CollectionAssert.DoesNotContain(results, loose, "the 15-character scatter must not be a candidate");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    [TestMethod]
    public void MidReadingStart_StaysOutOfBothModes()
    {
        // 简单输出式笔记架构: "huchu" opens inside 出's "chu" -- no reading starts with 'h' -- so no
        // spelling of this name starts where that match starts. The reachable forms all open on a
        // reading and consume adjacent characters.
        string[] names = ["简单输出式笔记架构.txt"];

        Assert.IsEmpty(Search("huchu", fuzzy: false, names));
        Assert.IsEmpty(Search("huchu", fuzzy: true, names));
        foreach (var query in new[] { "jdsc", "jiandansc", "jdshuc", "shuchu", "jiandan shuchu ji jgou jian" })
            CollectionAssert.Contains(Search(query, fuzzy: false, names), names[0], $"'{query}' opens on a reading");
    }

    // The setting must not change how PINYIN reads a name: same query, same names through the live
    // tier, both modes. What still differs by mode is the candidate's own text (exact wants a run in
    // the name, fuzzy a scattered subsequence of it), which is exactly what the setting means.
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
        // A whole reading is still reached from its own syllable boundary, and a half-typed trailing
        // reading still completes: "zhengsh" is 证 followed by the start of 书's "shu".
        Assert.IsNotEmpty(Search("zhengsh", fuzzy: false, Certificate), "'zhengsh' is a run of 证书's reading");
        Assert.IsNotEmpty(Search("zhengshu", fuzzy: false, Certificate));

        // "gsh" is NOT: 理 sits between 管 and 书 and cannot consume the "s", and a run may not skip
        // it -- the old skip-based reading is gone by design.
        Assert.IsEmpty(Search("gsh", fuzzy: false, Certificate));
        Assert.IsEmpty(Search("gsh", fuzzy: true, Certificate));
        Assert.IsEmpty(Search("ngsh", fuzzy: false, Certificate));
        Assert.IsEmpty(Search("ngsh", fuzzy: true, Certificate));
    }
}
