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

    // A real-world shape: an e-book export whose name is a Chinese title plus a long transliterated
    // metadata tail. Its full pinyin is longer than the combination path's per-variant cap, which used
    // to mean the capped walk completed nothing and the name baked NO alias at all -- unreachable by
    // every pinyin query, however short, while its literal Chinese text still matched.
    internal const string LongPolyphonicName =
        "防弹笔记法-简单输出式笔记架构,保护高产出、高效能心流 -- 电脑玩物站长Esor -- View zhi chang li, 2AB964, Chu ban, Tai bei shi, 2022 -- 城邦出版集团 创意市集 -- 9786267149096 -- df7686e59ff25513fb9c9ac4e906db6d -- Anna’s Archive.pdf";

    [TestMethod]
    public void GenerateAliases_LongPolyphonicName_KeepsItsInitialsAlias()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases(LongPolyphonicName);

        Assert.IsGreaterThan(0, aliases.Length, "an over-long name must not lose every alias");
        var initials = aliases[0].Split('|')[0];
        Assert.AreEqual(LongPolyphonicName.Length, initials.Length, "the initials alias is one character per source character");
        Assert.StartsWith("fdbjf", initials);
    }

    [TestMethod]
    public void GenerateAliases_LongPolyphonicName_KeepsOneFullSpellingPastTheCap()
    {
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases(LongPolyphonicName);

        // The cap bounds how many readings are enumerated, not how long a name may be: the fallback's
        // buffer grows, so the single-reading spelling is emitted even though it overruns the cap.
        Assert.HasCount(2, aliases);
        var full = aliases[1];
        Assert.IsGreaterThan(PinyinAliasCombinationGenerator.MaxComboFullPinyinLength, full.Length);
        Assert.StartsWith("fang", full);
    }

    [TestMethod]
    public void GenerateAliases_EnumerationBeyondTheStepsBudget_FallsBackInsteadOfReturningNothing()
    {
        // 55 x a two-reading character: every branch overruns the cap, so no leaf is ever reached and
        // the steps budget stops the walk. Same all-or-nothing cliff, reached by the other route.
        var text = new string('重', 55) + ".pdf";

        var aliases = PinyinAliasCombinationGenerator.GenerateAliases(text);

        Assert.AreEqual(2, aliases.Length);
        Assert.AreEqual(text.Length, aliases[0].Split('|')[0].Length);
    }

    [TestMethod]
    public void GenerateAliases_NameWithinTheCap_StillEnumeratesEveryReading()
    {
        // The fallback must not become the normal path: a short polyphonic name keeps its alternatives.
        var aliases = PinyinAliasCombinationGenerator.GenerateAliases("重建");

        Assert.HasCount(2, aliases[0].Split('|'), "both readings of 重 are still offered");
    }
}
