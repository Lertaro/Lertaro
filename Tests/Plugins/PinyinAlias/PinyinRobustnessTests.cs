using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The robustness suite: the real provider, baked by the real SnapshotWriter, searched by the real
// searcher -- over two deliberately hostile names (the ticket name and the reported variant with
// 笔abc记 / Chu效 / 高 2AB964能 interruptions). It pins, in BOTH matching modes:
//   * full-pinyin, initials-only and mixed blocks, in one token and across space-separated tokens;
//   * token runs that must stay CONTIGUOUS (WinShun semantics: no character inside a run is skipped);
//   * literal English/digit/symbol content, CJK runs, Traditional-to-Simplified query forms;
//   * the mode split: pinyin answers the same in both modes, scattered literal text is fuzzy-only;
//   * exclusions, highlight non-emptiness, and long multi-term sentences.
[TestClass]
public sealed class PinyinRobustnessTests
{
    private const string Ticket =
        "防弹笔记法-简单输出式笔记架构,保护高产出、高效能心流 -- 电脑玩物站长Esor -- View zhi chang li, 2AB964, Chu ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive";

    // The reported variant: its own interruptions (test, 笔abc记, 高 2AB964能, Chu效 ban) on top of it.
    private const string Messy =
        "防弹笔记法-test简单输出式笔abc记架构,保护高产出、高效能心流 --笔记 电脑玩物站长Esor -- View zhi chang li,高 2AB964能, Chu效 ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive.pdf";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    private static bool Reaches(string query, string name, bool fuzzy)
    {
        using var fixture = new RealIndexFixture(Ticket, Messy);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = fuzzy;
            return fixture.Search(query).Contains(name);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    private static void AssertReachesBothModes(string query, string name)
    {
        Assert.IsTrue(Reaches(query, name, fuzzy: false), $"'{query}' must reach {name} with fuzzy off");
        Assert.IsTrue(Reaches(query, name, fuzzy: true), $"'{query}' must reach {name} with fuzzy on");
    }

    private static void AssertReachesNothing(string query)
    {
        Assert.IsFalse(Reaches(query, Ticket, fuzzy: false), $"'{query}' exact must not reach the ticket name");
        Assert.IsFalse(Reaches(query, Ticket, fuzzy: true), $"'{query}' fuzzy must not reach the ticket name");
        Assert.IsFalse(Reaches(query, Messy, fuzzy: false), $"'{query}' exact must not reach the messy name");
        Assert.IsFalse(Reaches(query, Messy, fuzzy: true), $"'{query}' fuzzy must not reach the messy name");
    }

    // Every shape reaches BOTH names in both modes: whole readings, initials, one-initial-plus-whole
    // mixes, and the one mixed-script token (电nao) that goes through the spellings lane.
    [TestMethod]
    [DataRow("fdbjf")]
    [DataRow("fdan")]
    [DataRow("fangdanbijifa")]
    [DataRow("gxiao")]
    [DataRow("gaoxiaonengxinliu")]
    [DataRow("gcc")]
    [DataRow("cbcbjt")]
    [DataRow("chuangyishiji")]
    [DataRow("chuangysji")]
    [DataRow("cysj")]
    [DataRow("diannaowanwuzhanzhang")]
    [DataRow("dnwwzz")]
    [DataRow("nx")]
    [DataRow("xiaonxl")]
    [DataRow("zhangesor")]
    public void PinyinShapes_ReachBothNames(string query)
    {
        AssertReachesBothModes(query, Ticket);
        AssertReachesBothModes(query, Messy);
    }

    // A term mixing native-script characters with pinyin letters is answered by the mixed-alphabet lane,
    // which is a last-resort rescue for ONE bare fuzzy term. Both halves of that limit are load-bearing:
    // the lane matches an alias as text and has to say how its sub-match combines with the rest of a
    // multi-term query, and in an exact query it cannot tell a word's pinyin letters from letters that
    // are literal content of the name -- "笔ab记" against "笔abc记" re-reads the a/b as two initials and
    // reports a run the name does not contain. So exact matching answers a mixed term from the name's own
    // text, and only a name that literally carries it is found.
    [TestMethod]
    public void MixedAlphabetTerm_IsAFuzzyModeRescue()
    {
        Assert.IsTrue(Reaches("电nao", Ticket, fuzzy: true), "fuzzy matching reads 电 + 脑's initial run");
        Assert.IsTrue(Reaches("电nao", Messy, fuzzy: true));
        Assert.IsFalse(Reaches("电nao", Ticket, fuzzy: false), "the same query in exact mode wants the run in the name");
        Assert.IsTrue(Reaches("chuangysji Anna", Ticket, fuzzy: true));
        Assert.IsTrue(Reaches("chuangysji Anna 电nao", Ticket, fuzzy: true));
        Assert.IsFalse(Reaches("chuangysji Anna 电nao", Ticket, fuzzy: false), "and the mixed word has no lane to read it inside a multi-term query");
    }

    // Space-separated tokens are independent; a pinyin block, a literal CJK word and an English word
    // may live in different parts of the name (no same-spelling requirement).
    [TestMethod]
    [DataRow("fangdan baohu nx dnwwzz sj")]
    [DataRow("fdbjf gao")]
    [DataRow("fdbjf gxiao")]
    [DataRow("fdan xiaonxl chuangysji")]
    [DataRow("fdbjf 简单")]
    [DataRow("xinl gxiao 简单")]
    public void MultiTermTokens_AreIndependent(string query)
    {
        AssertReachesBothModes(query, Ticket);
        AssertReachesBothModes(query, Messy);
    }

    // The long-sentence shape: one query mixing pinyin blocks, a CJK word, an English word and digits.
    [TestMethod]
    public void LongMixedSentence_ReachesBothNames()
    {
        const string query = "fdbjf fdan gao 简单 Anna 9786267 df7686";
        AssertReachesBothModes(query, Ticket);
        AssertReachesBothModes(query, Messy);
    }

    // English, digits and symbols pass through literally, case-insensitively.
    [TestMethod]
    [DataRow("Anna")]
    [DataRow("Archive")]
    [DataRow("Esor")]
    [DataRow("view")]
    [DataRow("ban")]
    [DataRow("tai bei shi")]
    [DataRow("9786267")]
    [DataRow("df7686")]
    [DataRow("ab964")]
    [DataRow("简单输出式")]
    [DataRow("、高效能")]
    [DataRow("--")]
    [DataRow("Anna’s")]
    [DataRow("FDBJF")]
    [DataRow("ANNA")]
    [DataRow("Df7686")]
    public void LiteralAndSymbolContent_ReachesBothNames(string query)
    {
        AssertReachesBothModes(query, Ticket);
        AssertReachesBothModes(query, Messy);
    }

    // A Traditional query reaches the Simplified name through the provider's query forms.
    [TestMethod]
    [DataRow("創意市集")]
    [DataRow("創意")]
    public void TraditionalQuery_ReachesTheSimplifiedName(string query)
    {
        AssertReachesBothModes(query, Ticket);
        AssertReachesBothModes(query, Messy);
    }

    // The reference-semantics rejections, all in both modes:
    //   * a run interrupted by other characters (no skipping inside one token);
    //   * blocks out of the name's order inside one token;
    //   * a reading's tail glued to the next syllable's head ("ex", "piji");
    //   * a pinyin token crossing a separator ('-') between two blocks;
    //   * the reported "duc": its d/u/c only exist with gaps between them (single-token skipping was
    //     the OLD semantics; the whole example minus "duc" is pinned positive below).
    [TestMethod]
    [DataRow("fdbjfgxiao")]
    [DataRow("dnwwzzfdbjf")]
    [DataRow("ex")]
    [DataRow("piji")]
    [DataRow("fangdanbijifajiandan")]
    [DataRow("duc")]
    [DataRow("fdanbjfa cy Anna test duc hu")]
    public void RejectedShapes_ReachNothing(string query) => AssertReachesNothing(query);

    // "baj" is NOT rejected, and that is the behaviour the port is for: 版 gives up "ba" (a prefix of its
    // own "ban") and 集 gives "j", so the word is one contiguous run of the name's readings -- the same
    // shape the reference pins as correct ("bga" reaching 报告). It ranks where a mid-name four-point hit
    // belongs, below anything literal or boundary-aligned, which is the demotion the old span/score gate
    // was doing by hand.
    [TestMethod]
    public void HalfTypedReadingMidName_IsReadAndRankedLow()
    {
        Assert.IsTrue(Reaches("baj", Ticket, fuzzy: false));
        Assert.IsTrue(Reaches("baj", Ticket, fuzzy: true));
    }

    // The same example WITHOUT the unfindable "duc": everything else lands, in both modes. "test" is the
    // messy variant's own literal content -- the ticket name has no such word -- so that token is only
    // asserted there; the ticket answer is the same sentence with it dropped.
    [TestMethod]
    public void ReportedExample_MinusTheGappedToken_ReachesBothNames()
    {
        AssertReachesBothModes("fdanbjfa cy Anna test hu", Messy);
        AssertReachesBothModes("fdanbjfa cy Anna hu", Ticket);
    }

    // The mode split, pinned: pinyin is the same in both modes, while scattered literal text is what
    // fuzzy matching licenses -- not a spelling of the name.
    [TestMethod]
    public void ScatteredLiteralText_IsFuzzyOnly()
    {
        Assert.IsTrue(Reaches("防笔记法", Ticket, fuzzy: true));
        Assert.IsFalse(Reaches("防笔记法", Ticket, fuzzy: false));
        Assert.IsTrue(Reaches("笔ab记", Messy, fuzzy: true), "笔…记 with 'c' skipped is a scattered literal match");
        Assert.IsFalse(Reaches("笔ab记", Messy, fuzzy: false), "exact matching wants one continuous run");
    }

    // Exclusions: the excluded text vetoes on the name itself, and a satisfied-by-absence exclusion
    // lets the pinyin part carry the query.
    [TestMethod]
    public void Exclusions_ApplyToBothNames()
    {
        Assert.IsFalse(Reaches("fdbjf !简单", Ticket, fuzzy: false));
        Assert.IsFalse(Reaches("fdbjf !简单", Ticket, fuzzy: true));
        Assert.IsTrue(Reaches("!zzzz fdbjf", Ticket, fuzzy: false));
        Assert.IsTrue(Reaches("!zzzz fdbjf", Messy, fuzzy: true));
    }

    // A found row must light something up (an empty mask is NoMatch to the App's item gate), and a
    // pinyin match lights exactly the characters its run consumed.
    [TestMethod]
    public void Matches_LightTheConsumedCharacters()
    {
        using var fixture = new RealIndexFixture(Ticket, Messy);

        var lit = fixture.HighlightedIndices(Ticket, "fdan");
        CollectionAssert.Contains(lit, 0, "防 was consumed");
        CollectionAssert.Contains(lit, 1, "弹 was consumed");

        Assert.IsNotEmpty(fixture.HighlightedIndices(Ticket, "chuangysji"));
        Assert.IsNotEmpty(fixture.HighlightedIndices(Messy, "zhangesor"));
    }
}
