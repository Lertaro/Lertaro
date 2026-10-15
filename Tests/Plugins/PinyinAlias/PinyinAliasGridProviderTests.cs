using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The grid capability's contract is that its readings are the very alias space the string and byte
// generators already produce: the initials alias is one character per position and the full alias is
// the readings with the provider's boundaries between them. These tests hold the grid to what
// GenerateAliases actually yields for the same text, which is what keeps a baked index and a live scan
// from answering the same query differently.
[TestClass]
public sealed class PinyinAliasGridProviderTests
{
    private static readonly PinyinAliasProvider Provider = new();

    [TestMethod]
    public void GetReadingSets_DominantReadings_SpellTheInitialsAlias()
    {
        var text = "防弹笔记法";

        var sets = Provider.GetReadingSets(text).Take(text.Length).ToArray();
        var initials = string.Concat(sets.Select(s => s[0][0]));

        // The dominant-reading initials are one variant among the enumerated ones (this name is
        // polyphonic, so the string encoding emits a '|'-joined set of them).
        var variants = PinyinAliasCombinationGenerator.GenerateAliases(text).SelectMany(a => a.Split('|'));
        CollectionAssert.Contains(variants.ToList(), initials);
        Assert.AreEqual(text.Length, initials.Length);
        Assert.AreEqual("fdbjf", initials);
    }

    [TestMethod]
    public void GetReadingSets_DominantReadings_SpellTheFullAlias()
    {
        var text = "防弹笔记法";

        var sets = Provider.GetReadingSets(text);
        var full = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (Provider.NeedsSeparatorBefore(text, i))
                full.Append(PinyinAliasFormat.SyllableSeparator);
            full.Append(sets[i][0]);
        }

        var variants = PinyinAliasCombinationGenerator.GenerateAliases(text).SelectMany(a => a.Split('|'));
        CollectionAssert.Contains(variants.ToList(), full.ToString());
    }

    [TestMethod]
    public void GetReadingSets_EveryReadingIsNonEmptyAndLowercase()
    {
        var text = "中国A|b\uD83D\uDE00";

        foreach (var set in Provider.GetReadingSets(text).Take(text.Length))
        {
            Assert.IsTrue(set.Length > 0, "every position has at least one reading");
            foreach (var reading in set)
            {
                Assert.IsTrue(reading.Length > 0, "a reading is never empty");
                Assert.AreEqual(reading.ToLowerInvariant(), reading);
            }
        }
    }

    [TestMethod]
    public void GetReadingSets_PipePosition_UsesTheSamePlaceholderTheStringPathSubstitutes()
    {
        // '|' separates alternative readings inside a generated alias, so both encodings have to replace
        // a literal one in the name; a grid that passed it through would split one alias into two.
        var sets = Provider.GetReadingSets("中|");

        Assert.AreEqual("\u0001", sets[1][0]);
    }

    [TestMethod]
    public void GetReadingSets_SurrogatePair_KeepsBothHalvesSoMaterializationRePairsThem()
    {
        var text = "中\uD83D\uDE00";

        var sets = Provider.GetReadingSets(text);

        Assert.AreEqual("\uD83D", sets[1][0]);
        Assert.AreEqual("\uDE00", sets[2][0]);
        Assert.AreEqual("\uD83D\uDE00", string.Concat(sets[1][0], sets[2][0]));
    }

    [TestMethod]
    public void GetReadingSets_PolyphonicCharacter_CarriesEveryReadingOfTheAliasSpace()
    {
        var sets = Provider.GetReadingSets("重");

        Assert.IsTrue(sets[0].Length >= 2, "重 is polyphonic");
        CollectionAssert.Contains(sets[0], "zhong");
        CollectionAssert.Contains(sets[0], "chong");
    }

    [TestMethod]
    public void NeedsSeparatorBefore_SeparatesTwoTransliteratedPositionsOnly()
    {
        var withDigits = "第01集";
        var both = "中集";

        Assert.IsFalse(Provider.NeedsSeparatorBefore(withDigits, 0));
        Assert.IsFalse(Provider.NeedsSeparatorBefore(withDigits, 1), "a transliterated position meeting a digit keeps the alias glued");
        Assert.IsFalse(Provider.NeedsSeparatorBefore(withDigits, 3), "the digit before 集 already is the visible boundary");
        Assert.IsTrue(Provider.NeedsSeparatorBefore(both, 1), "two adjacent transliterated positions get a boundary");
    }

    private static string FakeConvert(string text) => text.Replace('電', '电').Replace('腦', '脑');

    [TestMethod]
    public void ReadingAndStandaloneHalves_ReassembleIntoExactlyWhatEncodeEmits()
    {
        var text = "電腦|A中";
        var expected = new AliasByteSink();
        PinyinAliasUtf8Encoder.Encode(text, expected, convertEnabled: true, FakeConvert);

        var split = new AliasByteSink();
        PinyinAliasUtf8PinyinEncoder.Encode(text, split);
        PinyinAliasUtf8Encoder.AppendSimplified(text, split, convertEnabled: true, FakeConvert);

        Assert.AreEqual(expected.SegmentCount, split.SegmentCount);
        for (var i = 0; i < expected.SegmentCount; i++)
            CollectionAssert.AreEqual(expected.Segment(i).ToArray(), split.Segment(i).ToArray());
    }

    [TestMethod]
    public void ProviderHalves_ReassembleIntoGetAliasesUtf8()
    {
        var text = "中国A";

        var expected = new AliasByteSink();
        Provider.GetAliasesUtf8(text, expected);

        var split = new AliasByteSink();
        Provider.GetReadingAliasesUtf8(text, split);
        Provider.GetStandaloneAliasesUtf8(text, split);

        Assert.AreEqual(expected.SegmentCount, split.SegmentCount);
        for (var i = 0; i < expected.SegmentCount; i++)
            CollectionAssert.AreEqual(expected.Segment(i).ToArray(), split.Segment(i).ToArray());
    }
}
