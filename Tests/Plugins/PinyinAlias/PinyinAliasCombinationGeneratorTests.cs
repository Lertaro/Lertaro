namespace Lertaro.Plugins.PinyinAlias.Tests;

// The reading sets that still feed the host: the per-position syllable lists behind
// IAliasLiveMatcherProvider.GetReadingSets, and the shapes they have to keep for the alias consumers --
// lowercased ASCII, a literal '|' substituted, one entry per character.
[TestClass]
public sealed class PinyinAliasCombinationGeneratorTests
{
    [TestMethod]
    public void GetSyllableLists_ChineseChar_ReturnsPinyinCandidates()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("中");

        CollectionAssert.Contains(lists[0], "zhong");
    }

    [TestMethod]
    public void GetSyllableLists_AsciiChar_ReturnsLowercasedSingleCharList()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("A");

        CollectionAssert.AreEqual(new[] { "a" }, lists[0]);
    }

    // Regression test: '|' is the one ASCII character every alias consumer
    // (HighlightMask.MarkViaAliasProviders, FzfPattern.TryMatch/IsAcceptableAliasMatch and the snapshot's
    // own alias blobs) treats as the separator between alternative polyphonic readings -- a source text
    // containing a literal '|' (e.g. a browser tab title like "example.com | 代理") must never pass that
    // character straight through into a generated alias, or downstream consumers misread it as a reading
    // boundary and silently lose the match/highlight for the text after it.
    [TestMethod]
    public void GetSyllableLists_PipeChar_DoesNotPassThroughLiterally()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("|");

        CollectionAssert.DoesNotContain(lists[0], "|");
    }
}
