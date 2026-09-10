using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Search;

// Ranking preference between the two alias shapes a transliterating provider emits for the same name:
// the per-character INITIALS shorthand must outrank the full readings. Searching "hao" must place
// "好爱哦耶.mp3" (whose initials "haoy..." carry the query across three characters) above "毫宅.psd"
// (reachable only through the full reading "hao...", where the three query letters collapse back onto a
// single source character). The weight that decides this is HighlightMask.ComputeWeight, measured on the
// mask mapped back onto the name -- so the initials form wins because it covers more of the name, not
// because of any pinyin-specific rule.
//
// App test project on purpose: a plugin test project must not reference Core, and Core's own tests run
// with no alias provider registered (several assert behaviour that depends on there being none).
[TestClass]
[DoNotParallelize] // registers into a process-wide registry
public sealed class AliasRankingTests
{
    private const char Sep = (char)2;

    private sealed class FakeReadingsProvider : IAliasProvider
    {
        private static readonly Dictionary<char, string> Readings = new()
        {
            ['好'] = "hao",
            ['爱'] = "ai",
            ['哦'] = "o",
            ['耶'] = "ye",
            ['毫'] = "hao",
            ['宅'] = "zhai",
            // The reported cross-syllable pair: 学习 = xue + xi and 人行道 = ren + hang + dao. Their INITIALS
            // aliases are "xx"/"rhd", so neither contains "ex" -- the old match came from "e" taking the
            // tail of xue/ren and "x" the head of xi (人行道's 行 is read xing here, giving the alias form
            // in the readings list below).
            ['学'] = "xue",
            ['习'] = "xi",
            ['人'] = "ren",
            ['行'] = "xing",
            ['道'] = "dao",
            // 恶性 = e + xing: its initials alias "ex" legitimately carries the abbreviation.
            ['恶'] = "e",
            ['性'] = "xing",
        };

        public string Name => "FakeReadings";
        public IReadOnlyList<(char Start, char End)> InputRanges { get; } = new[] { ('一', '鿿') };
        public IReadOnlyList<(char Start, char End)> OutputRanges { get; } = new[] { ('a', 'z') };

        // Declared, so the host enforces syllable alignment and can tell the two alias shapes apart for
        // tiering -- exactly the contract the real pinyin provider fills in.
        public char SyllableSeparator => Sep;

        public bool CanHandle(string text) => text.Any(Readings.ContainsKey);

        public IEnumerable<string> GetAliases(string text)
        {
            var parts = Parts(text);
            yield return string.Concat(parts.Select(p => p[0]));
            yield return string.Join(Sep, parts);
        }

        // Intentionally empty: "hao" reaches both aliases as a plain subsequence of the stored alias, so
        // no rewriting is needed -- and staying silent keeps this fake from adding query forms to every
        // other test in the process (FzfPattern.Parse asks every registered provider).
        public IEnumerable<string> GetQueryForms(string term) => Array.Empty<string>();

        public int[]? MapAliasToSourceIndices(string text, string alias)
        {
            var parts = Parts(text);
            if (alias.Length == text.Length)
                return Enumerable.Range(0, text.Length).ToArray();

            var map = new int[alias.Length];
            var pos = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    if (pos >= alias.Length || alias[pos] != Sep)
                        return null;
                    map[pos++] = i;
                }
                if (pos + parts[i].Length > alias.Length ||
                    string.CompareOrdinal(alias, pos, parts[i], 0, parts[i].Length) != 0)
                    return null;
                for (var j = 0; j < parts[i].Length; j++)
                    map[pos + j] = i;
                pos += parts[i].Length;
            }
            return pos == alias.Length ? map : null;
        }

        private static string[] Parts(string text) =>
            text.Select(c => Readings.TryGetValue(c, out var r) ? r : c.ToString()).ToArray();
    }

    private static bool _registered;

    [TestInitialize]
    public void Setup()
    {
        if (!_registered)
        {
            AliasProviderRegistry.Register(new FakeReadingsProvider());
            _registered = true;
        }
    }

    [TestMethod]
    public void InitialsMatch_OutweighsFullPinyinMatch()
    {
        var initials = FuzzyMatcher.ComputeMatchWeight("好爱哦耶.mp3", "hao");
        var fullPinyin = FuzzyMatcher.ComputeMatchWeight("毫宅.psd", "hao");

        Assert.IsGreaterThan(fullPinyin, initials);
    }

    [TestMethod]
    public void BothNames_MatchThePinyinQuery() 
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("hao", "好爱哦耶.mp3"));
        Assert.IsTrue(FuzzyMatcher.IsMatch("hao", "毫宅.psd"));
    }

    // The reported splice, with fuzzy matching OFF: "ex" must reach 恶性 (initials e+x) but not 学习
    // (xue+xi) or 人行道 (ren+xing), where the letters came from opposite ends of adjacent syllables.
    [TestMethod]
    public void Precise_InitialsQuery_MatchesTheAbbreviationButNotTheSplice()
    {
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            Assert.IsTrue(FuzzyMatcher.IsMatch("ex", "恶性.txt"), "initials e+x is the abbreviation the user typed");
            Assert.IsFalse(FuzzyMatcher.IsMatch("ex", "学习.txt"), "'e' from xue + 'x' from xi was a splice");
            Assert.IsFalse(FuzzyMatcher.IsMatch("ex", "人行道.txt"), "'e' from ren + 'x' from xing was a splice");
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    // A fragment that begins a syllable IS legitimate, so the rule constrains only where a match may start.
    [TestMethod]
    public void Precise_SyllableAlignedFragment_Matches()
    {
        SearchContext.FuzzyMatchEnabled = false;
        try
        {
            // "hao" is exactly 好's reading, and 毫宅's first syllable.
            Assert.IsTrue(FuzzyMatcher.IsMatch("hao", "好爱哦耶.mp3"));
            Assert.IsTrue(FuzzyMatcher.IsMatch("hao", "毫宅.psd"));
            // A later syllable is reachable from its own boundary: "ai" is 爱's whole reading.
            Assert.IsTrue(FuzzyMatcher.IsMatch("ai", "好爱哦耶.mp3"));
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = true;
        }
    }

    // The rule is for precise queries only -- with fuzzy on, the same loose read is what the user asked for.
    [TestMethod]
    public void Fuzzy_KeepsTheLooseBehaviour()
    {
        Assert.IsTrue(FuzzyMatcher.IsMatch("ex", "学习.txt"));
        Assert.IsTrue(FuzzyMatcher.IsMatch("ex", "人行道.txt"));
    }

    // Tier orders by HOW the match was found: a literal name hit beats an initials hit, which beats a full
    // reading. This is what makes 英文 > 简拼 > 全拼 instead of whichever alias happened to score higher.
    [TestMethod]
    public void Tier_LiteralBeatsInitialsBeatsFull()
    {
        var literal = FuzzyMatcher.ComputeMatchRank("hao.txt", "hao");
        var initials = FuzzyMatcher.ComputeMatchRank("好爱哦耶.mp3", "hao");
        var full = FuzzyMatcher.ComputeMatchRank("毫宅.psd", "hao");

        Assert.AreEqual(MatchRank.TierName, literal.Tier);
        Assert.AreEqual(MatchRank.TierInitials, initials.Tier);
        Assert.AreEqual(MatchRank.TierFull, full.Tier);
    }
}
