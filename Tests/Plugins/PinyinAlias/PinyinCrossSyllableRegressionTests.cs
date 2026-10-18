namespace Lertaro.Plugins.PinyinAlias.Tests;

// The seam corpus: entries whose readings would line up as "gsh" only ACROSS a syllable boundary
// (…zhen|shu, …xiang|she…). Under the live matcher a token is a contiguous run of adjacent
// characters, each consuming a prefix of one of its readings, so the seam letters can never line up
// -- while every legitimate spelling of the corpus still reaches its name.
//
// Corpus is generic Windows/technical terminology only -- no real file names, no personal paths (repo
// rule 15). The matcher under test is the provider's own word factory, exactly what the search
// pipeline consults.
[TestClass]
public sealed class PinyinCrossSyllableRegressionTests
{
    private static readonly PinyinAliasProvider Provider = new();

    // Entries whose concatenated pinyin contains "gsh" only ACROSS a syllable boundary.
    private static readonly string[] SeamCorpus =
    {
        "管理用户证书",   // …zhen|shu
        "个人证书",       // …zhen|shu
        "证书管理器",     // zhen|shu…
        "计算机证书",     // …zhen|shu
        "高级共享设置",   // …xiang|she…
        "防火墙设置",     // …qiang|she…
        "使用筛选键",     // …yong|shai…
        "应删除的文件",   // ying|shan…
        "日常识别",       // …chang|shi…
    };

    private static bool Matches(string query, string text)
    {
        var matcher = Provider.CreateMatcher(query, commonOnly: false);
        return matcher != null && matcher.TryFind(text, default, out _, out _);
    }

    [TestMethod]
    public void CrossSyllableQuery_MatchesNothing()
    {
        // "gsh" needs three adjacent characters whose readings start g / s / h -- none of these
        // entries has that run ("…g" is always followed by a character that cannot consume 's').
        var hits = SeamCorpus.Where(t => Matches("gsh", t)).ToList();

        Assert.IsEmpty(hits, $"still matched: {string.Join(", ", hits)}");
    }

    [TestMethod]
    public void RealMultiSyllableQuery_StillMatchesEverythingItShould()
    {
        // The recall half: the change must not cost a single legitimate hit.
        var expected = new[] { "管理用户证书", "个人证书", "证书管理器", "计算机证书" };

        CollectionAssert.AreEquivalent(expected, SeamCorpus.Where(t => Matches("zhengshu", t)).ToList());
    }

    [TestMethod]
    public void HalfTypedMultiSyllableQuery_StillMatches() =>
        // Search-as-you-type passes through this on the way to "zhengshu": a half-typed tail is a
        // valid prefix of 书's reading.
        Assert.IsTrue(Matches("zhengsh", "管理用户证书"));

    [TestMethod]
    public void SingleSyllableQuery_StillMatches()
    {
        Assert.IsTrue(Matches("zheng", "管理用户证书"));
        Assert.IsTrue(Matches("gaoji", "高级共享设置"));
    }

    [TestMethod]
    public void InitialsQuery_StillMatches()
    {
        // One character per position, all adjacent: the initials shape is the same walk with
        // one-letter pieces.
        Assert.IsTrue(Matches("glyhzs", "管理用户证书"));
        Assert.IsTrue(Matches("gjgxsz", "高级共享设置"));
    }

    [TestMethod]
    public void DigitsFollowedByPinyin_StillMatch() =>
        // Digits pass through as themselves, so a name mixing digits with CJK stays reachable by
        // typing what you can see.
        Assert.IsTrue(Matches("01ji", "第01集"));
}
