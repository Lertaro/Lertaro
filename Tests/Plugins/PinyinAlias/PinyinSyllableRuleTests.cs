using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The WinShun reading rule the architecture now follows: a match is a CONTIGUOUS run of name
// characters in which every character consumes at least one letter -- an ASCII character consumes
// itself (case folded), a Han character consumes any non-empty PREFIX of any of its readings (its
// initial, the whole syllable, or a half-typed run, anywhere in the term). 评价 (ping'jia) is the
// worked example: prefixes only, always starting where a reading starts, and a piece in the middle
// of a term may stop anywhere -- there is no "end of the term" restriction on a prefix.
//
// This is the decision, not a ranking detail, so it is asserted through the real pipeline (real
// provider, real baked snapshot, real searcher) in BOTH matching modes: pinyin is not a
// fuzzy/exact choice.
[TestClass]
public sealed class PinyinSyllableRuleTests
{
    private const string Name = "评价.txt";
    private const string Certificate = "管理用户证书.txt";

    [ClassInitialize]
    public static void RegisterRealProvider(TestContext context)
        => AliasProviderRegistry.Register(new PinyinAliasProvider());

    private static List<string> Search(string query, bool fuzzy, string name)
    {
        using var fixture = new RealIndexFixture(name);
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
    // Every one of these is a concatenation of prefixes of adjacent characters' readings, walked
    // left to right: "pj" abbreviates both characters, "pjia" abbreviates one and spells out the
    // other, "pij" stops 评's spelling halfway and then abbreviates 价 -- allowed anywhere.
    [DataRow("pj")]
    [DataRow("pingj")]
    [DataRow("pjia")]
    [DataRow("ping")]
    [DataRow("jia")]
    [DataRow("pingjia")]
    [DataRow("pingji")]
    [DataRow("pij")]
    [DataRow("piji")]
    [DataRow("pinj")]
    [DataRow("pijia")]
    public void PrefixSpellings_ReachTheName(string query)
    {
        CollectionAssert.Contains(Search(query, fuzzy: false, Name), Name, $"'{query}' with fuzzy off");
        CollectionAssert.Contains(Search(query, fuzzy: true, Name), Name, $"'{query}' with fuzzy on");
    }

    [TestMethod]
    // "ing"/"ia" open inside a reading (every piece has to start at its reading's first character),
    // "ja" is not a prefix of jia at all, single letters are literal-only (a pinyin word needs at
    // least two characters), and "zhengp" names no character of 评价.
    [DataRow("ing")]
    [DataRow("ia")]
    [DataRow("ja")]
    [DataRow("p")]
    [DataRow("j")]
    [DataRow("zhengp")]
    public void SpellingsThatOpenInsideAReadingOrAreTooShort_ReachNothing(string query)
    {
        Assert.IsEmpty(Search(query, fuzzy: false, Name), $"'{query}' with fuzzy off");
        Assert.IsEmpty(Search(query, fuzzy: true, Name), $"'{query}' with fuzzy on");
    }

    [TestMethod]
    // One contiguous run per term: a token cannot skip a name character, so every character in the
    // run has to be able to consume the next letter.
    [DataRow("gl")] // 管理 -- one initial per adjacent character
    [DataRow("zhengsh")] // 证书: whole reading plus a half-typed tail
    [DataRow("glyhzshu")] // 管理用户证书 as six adjacent pieces
    [DataRow("guanliyonghu")]
    public void ContiguousRuns_ReachTheCertificate(string query)
    {
        CollectionAssert.Contains(Search(query, fuzzy: false, Certificate), Certificate, $"{query} with fuzzy off");
        CollectionAssert.Contains(Search(query, fuzzy: true, Certificate), Certificate, $"{query} with fuzzy on");
    }

    [TestMethod]
    // These LOOK like abbreviations but are not: a middle character that cannot consume the next
    // letter (理 cannot give "s", 用 cannot give "z") breaks the run -- the old grid rule, which let
    // tokens skip name characters, accepted exactly this shape.
    [DataRow("gsh")]
    [DataRow("glzs")]
    [DataRow("guanlishu")]
    public void RunsThatWouldNeedSkipping_ReachNothing(string query)
    {
        Assert.IsEmpty(Search(query, fuzzy: false, Certificate), $"{query} with fuzzy off");
        Assert.IsEmpty(Search(query, fuzzy: true, Certificate), $"{query} with fuzzy on");
    }

    [TestMethod]
    // 贝 spells "bei": "ei" is not a prefix of bei, and its middle "i" is not the head of any piece.
    // 笔/比/必 really are spelled "bi".
    public void APieceMayNotBeSplicedOutOfAReading()
    {
        const string Bei = "4分贝.pdf";
        const string Bi = "4分笔.pdf";

        foreach (var fuzzy in new[] { false, true })
        {
            Assert.IsEmpty(Search("bi", fuzzy, Bei), $"'bi' spliced out of bei, fuzzy={fuzzy}");
            Assert.IsEmpty(Search("fbi", fuzzy, Bei), $"'f' + the spliced 'bi', fuzzy={fuzzy}");
            Assert.IsEmpty(Search("fei", fuzzy, Bei), "'ei' is not a prefix of bei either");

            CollectionAssert.Contains(Search("bi", fuzzy, Bi), Bi, $"'{Bi}' really is spelled bi");
            CollectionAssert.Contains(Search("fbi", fuzzy, Bi), Bi);
            CollectionAssert.Contains(Search("fbei", fuzzy, Bei), Bei);
            CollectionAssert.Contains(Search("bei", fuzzy, Bei), Bei);
        }
    }

    [TestMethod]
    // Independent of the query: the same terms on a different name must not suddenly stand for it.
    public void TheSameRefusedSpelling_StaysRefusedOnAnotherName()
    {
        Assert.IsEmpty(Search("piji", fuzzy: false, Certificate));
        Assert.IsEmpty(Search("zhengp", fuzzy: false, Name), "'zhengp' names no character of 评价");
    }

    [TestMethod]
    // The highlight tells the same story as the decision: it lights exactly the characters the
    // contiguous run consumed.
    [DataRow("北岸村邦出版.pdf", "bac", "0,1,2")]
    [DataRow("北岸村邦出版.pdf", "ba", "0,1")]
    [DataRow("北岸村邦出版.pdf", "bangc", "3,4")]
    [DataRow("邦出版.txt", "ban", "0")]
    [DataRow("邦出版.txt", "bcb", "0,1,2")]
    [DataRow("XX邦出版手册.pdf", "ba", "2")]
    [DataRow("XX邦出版手册.pdf", "bangc", "2,3")]
    public void HighlightLightsTheSyllablesTheWitnessConsumed(string name, string query, string expected)
    {
        var wanted = expected.Split(',').Select(int.Parse).ToList();
        foreach (var fuzzy in new[] { false, true })
        {
            var lit = Highlight(name, query, fuzzy);
            CollectionAssert.AreEqual(wanted, lit, $"'{query}' on {name}, fuzzy={fuzzy} lit {Describe(lit, name)}");
        }
    }

    private static List<int> Highlight(string name, string query, bool fuzzy)
    {
        using var fixture = new RealIndexFixture(name);
        var previous = SearchContext.FuzzyMatchEnabled;
        try
        {
            SearchContext.FuzzyMatchEnabled = fuzzy;
            return fixture.HighlightedIndices(name, query);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previous;
        }
    }

    private static string Describe(List<int> lit, string name)
        => "[" + string.Join(",", lit.Select(index => $"{index}:{name[index]}")) + "]";
}
