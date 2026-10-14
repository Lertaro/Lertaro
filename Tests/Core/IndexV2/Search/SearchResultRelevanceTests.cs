using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core.Tests.IndexV2.Search;

// How a name-mode result's RankSortKey is assembled. Key order, strongest first:
//   match shape > recency band > path depth > start position > quality weight > tier > files before folders
// Small key = better, which is how SearchResultRankComparer reads it.
[TestClass]
public sealed class SearchResultRelevanceTests
{
    [TestMethod]
    public void Apply_EarlierMatchInLongerName_RanksAboveLaterMatchInShorterName()
    {
        var leftmost = Ranked(@"C:\data\wxfef.doc", "wxfef.doc", "wx");
        var later = Ranked(@"C:\data\iwxfe.mp", "iwxfe.mp", "wx");

        // Coverage alone favours the shorter name; the start position must overrule it.
        Assert.IsLessThan(later, leftmost);
    }

    // The reported case: for "atlas", "code-atlas" is the whole word the user typed; "AutofillStates"
    // only holds a-t-l-a-s as scattered letters, even though they start at 0.
    [TestMethod]
    public void Apply_WholeWordLaterInName_RanksAboveScatteredMatchAtStart()
    {
        var wholeWord = Ranked(@"D:\dev\mcptools\code-atlas", "code-atlas", "atlas", isDir: true);
        var scattered = Ranked(@"C:\data\AutofillStates", "AutofillStates", "atlas");

        Assert.IsLessThan(scattered, wholeWord);
    }

    [TestMethod]
    public void Apply_Shapes_RankWholeWordThenWordStartThenInsideWordThenScattered()
    {
        var ordered = new[] { "code-atlas", "atlasing", "metatlas.txt", "annotatescrollbar.js" }
            .Select(name => Ranked($@"C:\data\{name}", name, "atlas"))
            .ToArray();

        for (var i = 1; i < ordered.Length; i++)
            Assert.IsLessThan(ordered[i], ordered[i - 1]);
    }

    [TestMethod]
    public void Apply_FileBeatsFolder_WhenTheMatchIsOtherwiseEqual()
    {
        var file = Ranked(@"C:\data\atlas", "atlas", "atlas");
        var folder = Ranked(@"C:\data\atlas", "atlas", "atlas", isDir: true);

        Assert.IsLessThan(folder, file);
    }

    // The reported case, second half: on this machine dozens of names start with "atlas" deep in game,
    // engine and package trees. Among equally good word matches the shallow project folder must win, even
    // though its match starts later in the name.
    [TestMethod]
    public void Apply_ShallowerPath_RanksAboveDeeperPath_EvenWithALaterStart()
    {
        var shallow = Ranked(@"D:\Projects\code-atlas", "code-atlas", "atlas", isDir: true);
        var deep = Ranked(@"Z:\Games\Hacknet\Content\Locales\de-de\DLC\Missions\Atlas.xml", "Atlas.xml", "atlas");

        Assert.IsLessThan(deep, shallow);
    }

    [TestMethod]
    public void Apply_RecentlyChanged_RanksAboveShallowerButOlder()
    {
        var now = DateTime.Now;
        var recentDeep = Ranked(@"C:\a\b\c\d\atlas.txt", "atlas.txt", "atlas", modified: now.AddHours(-2));
        var oldShallow = Ranked(@"C:\atlas.txt", "atlas.txt", "atlas", modified: now.AddYears(-1));

        Assert.IsLessThan(oldShallow, recentDeep);
    }

    // A file Chrome rewrites every minute must not ride recency past a real word match.
    [TestMethod]
    public void Apply_ScatteredMatchChangedToday_StillRanksBelowOldWholeWord()
    {
        var now = DateTime.Now;
        var scatteredToday = Ranked(@"C:\Chrome\AutofillStates", "AutofillStates", "atlas", modified: now);
        var wholeWordOld = Ranked(@"C:\a\b\c\d\e\code-atlas", "code-atlas", "atlas", modified: now.AddYears(-3));

        Assert.IsLessThan(scatteredToday, wholeWordOld);
    }

    [TestMethod]
    public void Apply_NameMatch_RanksAboveAncestorOnlyMatch()
    {
        var nameMatch = Ranked(@"C:\unrelated\xwxfef.txt", "xwxfef.txt", "wx");
        var ancestorOnly = Ranked(@"C:\wx_documents\report.txt", "report.txt", "wx");

        Assert.IsLessThan(ancestorOnly, nameMatch);
    }

    // These assert the key layout directly, because a real match cannot be made to produce an arbitrary
    // tier or an arbitrary combination of keys.
    [TestMethod]
    public void KeyLayout_ShapeOutranksStartWeightAndTier()
    {
        var wholeWordLate = Key(start: 200, weight: 0.01, tier: MatchRank.TierFull, shape: MatchRank.ShapeWholeWord, isDir: true);
        var wordStartPerfect = Key(start: 0, weight: 1, tier: MatchRank.TierName, shape: MatchRank.ShapeWordStart);

        Assert.IsLessThan(wordStartPerfect, wholeWordLate);
    }

    // Tier is the WEAKEST match key: it only separates rows that already agree on shape, start and weight.
    [TestMethod]
    public void KeyLayout_ShapeOutranksRecencyAndDepth()
    {
        var wholeWordOldDeep = Key(start: 0, weight: 1, tier: MatchRank.TierName, band: RankSignals.BandOlder, depth: 31);
        var wordStartTodayShallow = Key(start: 0, weight: 1, tier: MatchRank.TierName, shape: MatchRank.ShapeWordStart, band: RankSignals.BandToday);

        Assert.IsLessThan(wordStartTodayShallow, wholeWordOldDeep);
    }

    [TestMethod]
    public void KeyLayout_RecencyOutranksDepth()
    {
        var weekDeep = Key(start: 0, weight: 1, tier: MatchRank.TierName, band: RankSignals.BandWeek, depth: 20);
        var monthShallow = Key(start: 0, weight: 1, tier: MatchRank.TierName, band: RankSignals.BandMonth, depth: 1);

        Assert.IsLessThan(monthShallow, weekDeep);
    }

    [TestMethod]
    public void KeyLayout_DepthOutranksStartWeightAndTier()
    {
        var shallowLate = Key(start: 100, weight: 0.01, tier: MatchRank.TierFull, depth: 2, isDir: true);
        var deepPerfect = Key(start: 0, weight: 1, tier: MatchRank.TierName, depth: 3);

        Assert.IsLessThan(deepPerfect, shallowLate);
    }

    // Past the 5-bit cap, depth stops separating rows rather than spilling into the bits above it.
    [TestMethod]
    public void KeyLayout_DepthPastTheCapTiesInsteadOfOverflowing()
    {
        Assert.AreEqual(Key(start: 0, weight: 1, tier: 0, depth: 31), Key(start: 0, weight: 1, tier: 0, depth: 400));
        Assert.IsLessThan(Key(start: 0, weight: 1, tier: 0, shape: MatchRank.ShapeWordStart), Key(start: 0, weight: 1, tier: 0, depth: 400));
    }

    // Every other field is clamped the same way: past its own cap a row ties with the capped value and
    // never reaches the field above it, which would silently reorder whole groups of results.
    [TestMethod]
    public void KeyLayout_EveryOtherFieldSaturatesInsteadOfOverflowing()
    {
        Assert.AreEqual(Key(start: 127, weight: 1, tier: 0), Key(start: 400, weight: 1, tier: 0));
        Assert.AreEqual(Key(start: 0, weight: 1, tier: 0), Key(start: 0, weight: 9, tier: 0));
        Assert.AreEqual(Key(start: 0, weight: 0, tier: 0), Key(start: 0, weight: -9, tier: 0));
        Assert.AreEqual(Key(start: 0, weight: 1, tier: 0xF), Key(start: 0, weight: 1, tier: 40));
        Assert.AreEqual(Key(start: 0, weight: 1, tier: 0, shape: MatchRank.ShapeNoName), Key(start: 0, weight: 1, tier: 0, shape: 99));
        Assert.AreEqual(Key(start: 0, weight: 1, tier: 0, band: RankSignals.BandOlder), Key(start: 0, weight: 1, tier: 0, band: 99));
    }

    [TestMethod]
    public void KeyLayout_StartOutranksTier()
    {
        // start 3 / tier 0 (literal)  vs  start 0 / tier 2 (full pinyin): the earlier start wins.
        var laterLiteral = Key(start: 3, weight: 1, tier: MatchRank.TierName);
        var earlierFull = Key(start: 0, weight: 1, tier: MatchRank.TierFull);

        Assert.IsLessThan(laterLiteral, earlierFull);
    }

    [TestMethod]
    public void KeyLayout_WeightOutranksTier()
    {
        // Same start; the better weight wins even though its tier is the worst.
        var betterWeightWorseTier = Key(start: 0, weight: 0.9, tier: MatchRank.TierFull);
        var worseWeightBestTier = Key(start: 0, weight: 0.1, tier: MatchRank.TierName);

        Assert.IsLessThan(worseWeightBestTier, betterWeightWorseTier);
    }

    [TestMethod]
    public void KeyLayout_TierBreaksTiesOnEqualStartAndWeight()
    {
        var literal = Key(start: 0, weight: 0.5, tier: MatchRank.TierName);
        var initials = Key(start: 0, weight: 0.5, tier: MatchRank.TierInitials);
        var full = Key(start: 0, weight: 0.5, tier: MatchRank.TierFull);

        Assert.IsLessThan(initials, literal);
        Assert.IsLessThan(full, initials);
    }

    [TestMethod]
    public void KeyLayout_TierOutranksFolderBit()
    {
        var literalFolder = Key(start: 0, weight: 0.5, tier: MatchRank.TierName, isDir: true);
        var initialsFile = Key(start: 0, weight: 0.5, tier: MatchRank.TierInitials);

        Assert.IsLessThan(initialsFile, literalFolder);
    }

    private static uint Key(int start, double weight, int tier, int shape = MatchRank.ShapeWholeWord, bool isDir = false, int band = RankSignals.BandOlder, int depth = 0) =>
        SearchResultRelevance.HighKey(new MatchRank(tier, start, weight, shape), isDir, band, depth);

    [TestMethod]
    public void Apply_EmptyPattern_LeavesRankUnchanged()
    {
        var result = new SearchResult { Name = "readme.md", Path = @"C:\readme.md" };
        result.RankSortKey = 0x1234_5678_9ABC_DEF0UL;

        SearchResultRelevance.Apply(result, FzfPattern.Parse(""));

        Assert.AreEqual(0x1234_5678_9ABC_DEF0UL, result.RankSortKey);
    }

    // The engine's own span/length bits in the low 32 must survive: they are the deepest tie-breaker and
    // dropping them would make equally-ranked rows fall back to insertion order.
    [TestMethod]
    public void Apply_PreservesTheEnginesLowBits()
    {
        var result = new SearchResult { Name = "readme.md", Path = @"C:\readme.md" };
        result.RankSortKey = 0xFFFF_FFFF_DEAD_BEEFUL;

        SearchResultRelevance.Apply(result, FzfPattern.Parse("read"));

        Assert.AreEqual(0xDEAD_BEEFUL, result.RankSortKey & 0xFFFF_FFFFUL);
    }

    private static ulong Ranked(string path, string name, string query, bool isDir = false, DateTime? modified = null)
    {
        var result = new SearchResult { Name = name, Path = path, IsDir = isDir };
        if (modified is { } time)
            result.Metadata = new FileMetadata(0, time, time, time);
        SearchResultRelevance.Apply(result, FzfPattern.Parse(query));
        return result.RankSortKey;
    }
}
