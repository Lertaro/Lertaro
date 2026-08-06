namespace Lertaro.Plugins.PinyinAlias.Tests;

// End-to-end guard for the bug this whole format change exists for: a query that is not a syllable
// sequence used to match the full-pinyin alias by straddling a syllable boundary, dragging a screenful
// of unrelated entries into the results.
//
// Corpus is generic Windows/technical terminology only -- no real file names, no personal paths (repo
// rule 15). What matters is the shape: each entry ends one syllable in "g"/"n" and starts the next
// with "sh", which is the seam "gsh" used to hide in.
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

    // Matching as the search pipeline does it: the query, plus whatever spellings the provider offers
    // for it, against the provider's own generated aliases.
    private static bool Matches(string query, string text)
    {
        var forms = Provider.GetQueryForms(query).ToList();
        foreach (var alias in Provider.GetAliases(text))
        {
            if (alias.Contains(query, StringComparison.Ordinal))
                return true;
            if (forms.Any(f => alias.Contains(f, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    [TestMethod]
    public void CrossSyllableQuery_MatchesNothing()
    {
        // "gsh" is not a syllable sequence, so it gets no delimited spelling and cannot reach the full
        // pinyin. It is not in the initials alias of any of these either.
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
        // Search-as-you-type passes through this on the way to "zhengshu".
        Assert.IsTrue(Matches("zhengsh", "管理用户证书"));

    [TestMethod]
    public void SingleSyllableQuery_StillMatches()
    {
        Assert.IsTrue(Matches("zheng", "管理用户证书"));
        Assert.IsTrue(Matches("gaoji", "高级共享设置"));
    }

    [TestMethod]
    public void InitialsQuery_StillMatchesViaTheInitialsAlias()
    {
        // Untouched by the change: one character per source character, so every position in it is
        // already a boundary and nothing can hide inside it.
        Assert.IsTrue(Matches("glyhzs", "管理用户证书"));
        Assert.IsTrue(Matches("gjgxsz", "高级共享设置"));
    }

    [TestMethod]
    public void DigitsFollowedByPinyin_StillMatch() =>
        // The boundary is only placed between two adjacent transliterated characters, so a name mixing
        // digits with CJK keeps its alias unsplit and stays reachable by typing what you can see.
        Assert.IsTrue(Matches("01ji", "第01集"));
}
