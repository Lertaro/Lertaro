namespace Lertaro.Plugins.PinyinAlias.Tests;

[TestClass]
public sealed class PinyinAliasCombinationGeneratorTests
{
    [TestMethod]
    public void GenerateAliases_SingleChineseChar_ReturnsPinyinsDirectly()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中");

        CollectionAssert.Contains(aliases, "zhong");
    }

    [TestMethod]
    public void GenerateAliases_TwoCharMonophonicWord_ReturnsInitialsAndFull()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中国");

        CollectionAssert.Contains(aliases, "zg"); // initials: one char per source char, no boundaries
        // Full pinyin carries a syllable boundary between the two transliterated characters, so a
        // query cannot match across the seam ("ngg" is not a thing anyone typed).
        CollectionAssert.Contains(aliases, "zhong" + PinyinAliasFormat.SyllableSeparator + "guo");
    }

    [TestMethod]
    public void GenerateAliases_EveryAlias_IsLowercaseAscii()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中国人");

        foreach (var alias in aliases)
        {
            foreach (var part in alias.Split('|'))
            {
                Assert.IsTrue(part.Length == 0 || System.Text.Ascii.IsValid(part));
                Assert.AreEqual(part.ToLowerInvariant(), part);
            }
        }
    }

    [TestMethod]
    public void GenerateAliases_MixedChineseAndAscii_KeepsAsciiCharsLiteralAndLowercased()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中ABC");

        CollectionAssert.Contains(aliases, "zabc");
    }

    [TestMethod]
    public void GenerateAliases_NonChineseNonAsciiChar_LowercasedLiterally()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("中É");

        Assert.IsTrue(aliases.Any(a => a.EndsWith('é')));
    }

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

    // Regression test: '|' is the one ASCII character every alias consumer (JoinUnique here,
    // HighlightMask.MarkViaAliasProviders, FzfPattern.TryMatch/IsAcceptableAliasMatch) treats as the
    // separator between alternative polyphonic readings -- a source text containing a literal '|'
    // (e.g. a browser tab title like "example.com | 代理") must never pass that character straight
    // through into a generated alias, or downstream consumers misread it as a reading boundary and
    // silently lose the match/highlight for the text after it.
    [TestMethod]
    public void GetSyllableLists_PipeChar_DoesNotPassThroughLiterally()
    {
        var lists = PinyinAliasCombinationGenerator.GetSyllableLists("|");

        CollectionAssert.DoesNotContain(lists[0], "|");
    }

    [TestMethod]
    public void GenerateAliases_TextContainingLiteralPipe_NeverEmitsLiteralPipeCharacter()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("id | 中");

        foreach (var alias in aliases)
            Assert.DoesNotContain("|", alias);
    }
}
