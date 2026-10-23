using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.Tests.SearchIndex;

// Full-window score, section and history-tier decoration.
[TestClass]
public sealed class SearchResultRankingTests
{
    private const int SectionMask = 3;
    private const int StrongBit = 4;

    // A perfect name hit packs to high bits 0 ("smaller is better"), so a real row always carries the
    // engine's own tie-breakers in its low 32 bits. Without them the key would be all zero, which is the
    // decorator's "never ranked" marker and reads as weak on purpose.
    private const ulong EngineTieBits = 1UL;

    private static SearchResult Result(string path, ulong key = 0, bool isDir = false) =>
        new()
        {
            Name = System.IO.Path.GetFileName(path),
            Path = path,
            IsDir = isDir,
            Drive = "D",
            RankSortKey = key,
        };

    private static ulong StrongKey() =>
        ((ulong)SearchResultRelevance.HighKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord)) << 32)
        | EngineTieBits;

    private static ulong WeakKey() =>
        ((ulong)SearchResultRelevance.HighKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeScattered)) << 32)
        | EngineTieBits;

    private static Func<string, bool, PriorityVerdict> Rules(IndexRuleEffect effect, bool alwaysTop = false) =>
        (_, _) => new PriorityVerdict(effect, alwaysTop);

    private static RankContext Context(
        IndexRuleEffect effect = IndexRuleEffect.Normal,
        bool alwaysTop = false,
        int scoreX100 = 0,
        string scorePath = @"D:\dir\a.txt",
        string? favorite = null)
    {
        var scores = scoreX100 == 0
            ? null
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [scorePath] = scoreX100 };
        var favorites = favorite == null
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { favorite };
        return new RankContext(Rules(effect, alwaysTop), scores, favorites);
    }

    [TestMethod]
    public void Decorate_StrongNormalMatch_Scores18()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context());

        Assert.AreEqual(18, result.RankScore);
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(result.RankFlags & SectionMask));
        Assert.AreEqual(StrongBit, result.RankFlags & StrongBit);
        Assert.AreEqual(0, (result.RankFlags >> 3) & 3);
    }

    [TestMethod]
    public void Decorate_WeakNormalMatch_Scores8()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context());

        Assert.AreEqual(8, result.RankScore);
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(result.RankFlags & SectionMask));
        Assert.AreEqual(0, result.RankFlags & StrongBit);
    }

    [TestMethod]
    public void Decorate_StrongHighPriority_Scores26()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.High));

        Assert.AreEqual(26, result.RankScore);
    }

    [TestMethod]
    public void Decorate_WeakHighPriority_Scores16()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.High));

        Assert.AreEqual(16, result.RankScore);
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(result.RankFlags & SectionMask));
    }

    [TestMethod]
    public void Decorate_StrongLowPriority_IsLowSectionWithScore10()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.Low));

        Assert.AreEqual(10, result.RankScore);
        Assert.AreEqual((byte)RankSection.Low, (byte)(result.RankFlags & SectionMask));
    }

    [TestMethod]
    public void Decorate_ZeroKey_IsNotStrong()
    {
        // Never packed: the legacy ladder sorted this as the worst quality, so it must not claim the
        // strong-match score -- nor a history tier, which requires a strong match.
        var result = Result(@"D:\dir\a.txt", key: 0);

        SearchResultRanking.Decorate(result, Context(scoreX100: 900));

        Assert.AreEqual(8, result.RankScore);
        Assert.AreEqual(0, result.RankFlags & StrongBit);
        Assert.AreEqual((byte)HistoryTier.None, (byte)((result.RankFlags >> 3) & 3));
        Assert.AreEqual(900, result.HistoryScoreX100);
    }

    [TestMethod]
    public void Decorate_FavoriteStrongMatch_Scores44()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(favorite: @"D:\dir\a.txt"));

        Assert.AreEqual(44, result.RankScore);
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(result.RankFlags & SectionMask));
    }

    [TestMethod]
    public void Decorate_FavoriteWeakMatch_KeepsBaseScore()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context(favorite: @"D:\dir\a.txt"));

        Assert.AreEqual(8, result.RankScore);
    }

    [TestMethod]
    public void Decorate_HistoryH5_IsBoosted()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(scoreX100: 900));

        Assert.AreEqual(34, result.RankScore);
        Assert.AreEqual((byte)HistoryTier.H5, (byte)((result.RankFlags >> 3) & 3));
        Assert.AreEqual(900, result.HistoryScoreX100);
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(result.RankFlags & SectionMask));
    }

    [TestMethod]
    public void Decorate_HistoryH5_OnWeakMatch_IsIgnored()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context(scoreX100: 900));

        Assert.AreEqual(8, result.RankScore);
        Assert.AreEqual((byte)HistoryTier.None, (byte)((result.RankFlags >> 3) & 3));
    }

    [TestMethod]
    public void Decorate_HistoryBetween300And500_IsH3()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(scoreX100: 350));

        Assert.AreEqual(22, result.RankScore);
        Assert.AreEqual((byte)HistoryTier.H3, (byte)((result.RankFlags >> 3) & 3));
    }

    [TestMethod]
    public void Decorate_HistoryBelow300_StaysNone()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(scoreX100: 250));

        Assert.AreEqual(18, result.RankScore);
        Assert.AreEqual((byte)HistoryTier.None, (byte)((result.RankFlags >> 3) & 3));
    }

    [TestMethod]
    public void Decorate_AlwaysTopWeak_IsPinned()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.High, alwaysTop: true));

        Assert.AreEqual((byte)RankSection.Pinned, (byte)(result.RankFlags & SectionMask));
        Assert.AreEqual(16, result.RankScore);
    }

    [TestMethod]
    public void Decorate_IsIdempotent()
    {
        var context = Context(IndexRuleEffect.High, scoreX100: 620);
        var once = Result(@"D:\dir\a.txt", StrongKey());
        SearchResultRanking.Decorate(once, context);

        // A second row, decorated twice with the same context: the fields are written, not accumulated,
        // so a re-decoration (the accumulator does one when the context arrives) cannot drift.
        var twice = Result(@"D:\dir\a.txt", StrongKey());
        SearchResultRanking.Decorate(twice, context);
        SearchResultRanking.Decorate(twice, context);

        Assert.AreEqual(once.RankScore, twice.RankScore);
        Assert.AreEqual(once.RankFlags, twice.RankFlags);
        Assert.AreEqual(once.HistoryScoreX100, twice.HistoryScoreX100);
    }

    [TestMethod]
    public void Decorate_TrimsTrailingSeparatorForLookups()
    {
        var result = Result(@"D:\dir\", key: StrongKey(), isDir: true);

        SearchResultRanking.Decorate(result, Context(scoreX100: 900, scorePath: @"d:\DIR"));

        Assert.AreEqual(34, result.RankScore);
        Assert.AreEqual((byte)HistoryTier.H5, (byte)((result.RankFlags >> 3) & 3));
    }
}
