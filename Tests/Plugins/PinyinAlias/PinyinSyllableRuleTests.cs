using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The rule a typed term has to obey to be a spelling of a candidate: every piece of it either spells one
// character's reading out in full, abbreviates that character to its initial, or is the half-typed start
// of the LAST character it names. 评价 (ping'jia) is the worked example, straight from the ticket.
//
// This is the decision, not a ranking detail, so it is asserted through the real pipeline (real provider,
// real baked snapshot, real searcher) in BOTH matching modes: pinyin is not a fuzzy/exact choice.
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
    // Every one of these is a concatenation of whole readings and initials: "pj" abbreviates both
    // characters, "pingj" spells the first out and abbreviates the second, "pjia" does the reverse.
    [DataRow("pj")]
    [DataRow("pingj")]
    [DataRow("pjia")]
    [DataRow("ping")]
    [DataRow("p")]
    [DataRow("j")]
    [DataRow("jia")]
    [DataRow("pingjia")]
    [DataRow("pingji")]
    public void SpellingsThatStartWhereAReadingStarts_ReachTheName(string query)
    {
        CollectionAssert.Contains(Search(query, fuzzy: false, Name), Name, $"'{query}' with fuzzy off");
        CollectionAssert.Contains(Search(query, fuzzy: true, Name), Name, $"'{query}' with fuzzy on");
    }

    [TestMethod]
    // "ing"/"ia" open inside a reading, "ja" skips jia's middle, and "piji" is the shape that needs the
    // whole rule: "pi" is a real prefix of ping and "ji" a real prefix of jia, but a character is
    // abbreviated to its initial or spelled out, never left half-typed with more to come.
    [DataRow("ing")]
    [DataRow("ia")]
    [DataRow("ja")]
    [DataRow("piji")]
    [DataRow("pij")]
    [DataRow("pinj")]
    [DataRow("pijia")]
    public void SpellingsThatStartInsideAReadingOrLeaveOneHalfTyped_ReachNothing(string query)
    {
        Assert.IsEmpty(Search(query, fuzzy: false, Name), $"'{query}' with fuzzy off");
        Assert.IsEmpty(Search(query, fuzzy: true, Name), $"'{query}' with fuzzy on");
    }

    [TestMethod]
    // Skipping characters is not the same as skipping their readings: a multi-character name is named by
    // whichever of its characters the user spelled, in order.
    [DataRow("gl", "管理用户证书.txt")]
    [DataRow("gsh", "管理用户证书.txt")]
    [DataRow("glzs", "管理用户证书.txt")]
    [DataRow("guanlishu", "管理用户证书.txt")]
    [DataRow("glyhzshu", "管理用户证书.txt")]
    public void CharactersTheUserDidNotName_MayBeSkipped(string query, string name)
    {
        CollectionAssert.Contains(Search(query, fuzzy: false, name), name, $"{query} with fuzzy off");
        CollectionAssert.Contains(Search(query, fuzzy: true, name), name, $"{query} with fuzzy on");
    }

    [TestMethod]
    // 贝 spells "bei", and its middle "i" is not a piece of its own: neither "bi" nor "fbi" (an f, then
    // that spliced "bi") may reach it. The old boundary-only reading did exactly that -- it pinned where a
    // match STARTS and let every later character fall anywhere, so "b" opened 贝 and "i" came out of the
    // same reading's middle. 笔/比/必 are spelled "bi" for real, which is why the initials alias alone
    // cannot tell them from 贝 and why the rule has to be about readings.
    public void APieceMayNotBeSplicedOutOfAReadingItDoesNotStart()
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
    // The highlight tells the same story as the decision, character for character. This is the ticket's
    // `bac`: 北岸村 is a real spelling of it (three initials), 邦出版 is not -- and the painting used to
    // light 邦出 as well, because it re-searched the materialized alias (which carries EVERY syllable of
    // the name) and let the term align somewhere other than the reading choice that matched.
    //
    // "XX邦出版手册.pdf" + "bangc" is the same defect from the other side: the loose re-search found
    // nothing, so a row the engine had matched rendered with an empty mask -- and an empty mask is NoMatch
    // to the App's item gate.
    [DataRow("北岸村邦出版.pdf", "bac", "0,1,2")]
    [DataRow("北岸村邦出版.pdf", "ba", "0,1")]
    [DataRow("北岸村邦出版.pdf", "bcb", "0,2,3")]
    [DataRow("北岸村邦出版.pdf", "bangc", "3,4")]
    [DataRow("邦出版.txt", "ban", "0")]
    [DataRow("邦出版.txt", "bcb", "0,1,2")]
    [DataRow("XX邦出版手册.pdf", "ba", "2")]
    [DataRow("XX邦出版手册.pdf", "bangc", "2,3")]
    [DataRow("bac邦出版.txt", "bcb", "0,2,3")]
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
