using System.Text;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The live matching capability (IAliasLiveMatcherProvider): the matcher factory, the reading sets
// that feed the bake-time prefilter mask, and the single-reading spellings the mixed-alphabet lane
// matches as text.
[TestClass]
public sealed class PinyinAliasLiveProviderTests
{
    private static readonly PinyinAliasProvider Provider = new();

    [TestMethod]
    public void CreateMatcher_EligibilityMatchesTheWordRule()
    {
        Assert.IsNull(Provider.CreateMatcher("a", false), "a single letter is not a pinyin word");
        Assert.IsNull(Provider.CreateMatcher("12", false), "digits alone are not a pinyin word");
        Assert.IsNull(Provider.CreateMatcher("a-b", false));
        Assert.IsNull(Provider.CreateMatcher(new string('a', 64), false), "over 63 letters");
        Assert.IsNotNull(Provider.CreateMatcher("ab", false));
        Assert.IsNotNull(Provider.CreateMatcher("ab1", false));
        Assert.IsNotNull(Provider.CreateMatcher(new string('a', 63), false));
    }

    [TestMethod]
    public void CreateMatcher_FindsAcrossAdjacentReadings()
    {
        var matcher = Provider.CreateMatcher("bg", false);
        Assert.IsNotNull(matcher);

        Assert.IsTrue(matcher.TryFind("报告", default, out var start, out var end));
        Assert.AreEqual(0, start);
        Assert.AreEqual(2, end);
    }

    [TestMethod]
    public void CreateMatcher_CommonModeAcceptsTwoReadings()
    {
        // 重复 reads chong (重's second reading), so cfwj spans all four characters.
        var matcher = Provider.CreateMatcher("cfwj", true);
        Assert.IsNotNull(matcher);

        Assert.IsTrue(matcher.TryFind("重复文件2", default, out var start, out var end));
        Assert.AreEqual(0, start);
        Assert.AreEqual(4, end);
    }

    [TestMethod]
    public void CommonMatcher_ReportsTheTwoReadingMatchAsInitials()
    {
        var evaluator = PinyinNameEvaluator.Build(
            FzfPattern.Parse("cfwj"),
            new IAliasLiveMatcherProvider[] { Provider });

        Assert.IsTrue(evaluator.TryPaintTerm(
            "重复文件2", "cfwj", FzfTermKind.Exact, new bool[5], out var tier));
        Assert.AreEqual(MatchRank.TierInitials, tier, "重's second reading makes this TierInitials");
    }

    [TestMethod]
    public void GetReadingSets_EveryReadingIsNonEmptyAndLowercase()
    {
        var text = "中国A|b😀";

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
        // '|' separates alternative readings inside a generated alias, so both encodings have to
        // replace a literal one in the name; a reading set that passed it through would split one
        // alias into two.
        var sets = Provider.GetReadingSets("中|");

        Assert.AreEqual("", sets[1][0]);
    }

    [TestMethod]
    public void GetReadingSets_SurrogatePair_KeepsBothHalvesSoMaterializationRePairsThem()
    {
        var text = "中😀";

        var sets = Provider.GetReadingSets(text);

        Assert.AreEqual("\uD83D", sets[1][0]);
        Assert.AreEqual("\uDE00", sets[2][0]);
        Assert.AreEqual("😀", string.Concat(sets[1][0], sets[2][0]));
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
        // The format's own rule, unchanged by the live switch: the shared helper behind
        // AppendSpellings' full spelling.
        var withDigits = "第01集";
        var both = "中集";

        Assert.IsFalse(PinyinAliasFormat.NeedsSeparatorBefore(withDigits, 0));
        Assert.IsFalse(PinyinAliasFormat.NeedsSeparatorBefore(withDigits, 1), "a transliterated position meeting a digit keeps the alias glued");
        Assert.IsFalse(PinyinAliasFormat.NeedsSeparatorBefore(withDigits, 3), "the digit before 集 already is the visible boundary");
        Assert.IsTrue(PinyinAliasFormat.NeedsSeparatorBefore(both, 1), "two adjacent transliterated positions get a boundary");
    }

    [TestMethod]
    public void AppendSpellings_EmitsTheInitialsAndTheFullSingleReadingSpelling()
    {
        var dest = new List<string>();
        Provider.AppendSpellings("防弹笔记法", dest);

        Assert.HasCount(2, dest);
        Assert.AreEqual("fdbjf", dest[0]);
        var sep = PinyinAliasFormat.SyllableSeparator;
        Assert.AreEqual($"fang{sep}dan{sep}bi{sep}ji{sep}fa", dest[1], "the first reading of each position, boundaries where the format places them");
    }

    [TestMethod]
    public void AppendSpellings_EmptyText_EmitsNothing()
    {
        var dest = new List<string>();
        Provider.AppendSpellings("", dest);

        Assert.IsEmpty(dest);
    }
}
