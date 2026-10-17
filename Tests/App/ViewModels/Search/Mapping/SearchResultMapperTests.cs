using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RankAndDedupeTests
{
    private static AppSearchResult Result(string path, bool isDir = false) => new() { FullPath = path, Name = path, IsDir = isDir };

    private static SearchResultMapper.RankedCandidate Candidate(
        string path,
        bool isCurated = false,
        int priority = int.MaxValue,
        int typeRank = int.MaxValue,
        double weight = 0,
        string? normalizedPath = null,
        int start = 0,
        int tier = MatchRank.TierName,
        int shape = MatchRank.ShapeWholeWord,
        bool isDir = false,
        int band = 0,
        int depth = 0) =>
        new(Result(path, isDir), isCurated, priority, typeRank, new MatchRank(tier, start, weight, shape), normalizedPath ?? path, band, depth);

    // The report that set this priority: on a broad query, letting shape or recency outrank position pushed
    // the leftmost hit past the display cap and the row the user wanted disappeared. So a hit at index 0 of
    // "AutofillStates" leads a whole-word hit five characters in, whatever the shape or weight says.
    [TestMethod]
    public void RankAndDedupe_EarlierStartBeatsBetterShapeAndWeight()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\AutofillStates", shape: MatchRank.ShapeScattered, start: 0, weight: 0.4),
            Candidate(@"C:\atlasing", shape: MatchRank.ShapeWordStart, start: 0, weight: 0.6),
            Candidate(@"D:\code-atlas", shape: MatchRank.ShapeWholeWord, start: 5, weight: 0.5),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        // Start leads; among the two rows hitting at 0, coverage*contiguity decides.
        CollectionAssert.AreEqual(
            new[] { @"C:\atlasing", @"C:\AutofillStates", @"D:\code-atlas" },
            ranked.Select(r => r.FullPath).ToArray());
    }

    // Depth and recency are still computed and carried, but since that report they order nothing: a shallow
    // folder no longer beats a deeper row the query hit further left.
    [TestMethod]
    public void RankAndDedupe_EarlierStartBeatsShallowerPath()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate("deep", start: 0, weight: 0.6, depth: 11),
            Candidate("shallow", start: 5, weight: 0.5, depth: 3),
        };

        Assert.AreEqual("deep", SearchResultMapper.RankAndDedupe(candidates)[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_FileBeatsFolder_WhenTheMatchIsOtherwiseEqual()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a", weight: 0.5, isDir: true),
            Candidate(@"C:\b", weight: 0.5),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\b", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_CuratedBeatsUncuratedRegardlessOfWeight()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\uncurated", isCurated: false, weight: 100),
            Candidate(@"C:\curated", isCurated: true, weight: 0),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\curated", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AmongCurated_LowerPriorityValueWinsFirst()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\second", isCurated: true, priority: 5),
            Candidate(@"C:\first", isCurated: true, priority: 1),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\first", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_SamePriority_LowerTypeRankWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\typeB", typeRank: 2),
            Candidate(@"C:\typeA", typeRank: 1),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\typeA", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_SameTypeRank_HigherWeightWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\lowWeight", weight: 0.3),
            Candidate(@"C:\highWeight", weight: 0.9),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\highWeight", ranked[0].FullPath);
    }

    // Left-side match priority sits ABOVE weight, so a match beginning earlier wins even when the
    // competing (shorter-named) match has the larger coverage weight -- the same rule the full window's
    // relevance key applies.
    [TestMethod]
    public void RankAndDedupe_EarlierStartBeatsHigherWeight()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\iwxfe.mp", start: 1, weight: 0.25),
            Candidate(@"C:\wxfef.doc", start: 0, weight: 0.22),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\wxfef.doc", ranked[0].FullPath);
    }

    // Tier is the WEAKEST key: an earlier start wins even when a later match was found through a better
    // tier (英文 > 简拼 > 全拼 only separates rows that already agree on position and coverage).
    [TestMethod]
    public void RankAndDedupe_EarlierStartBeatsBetterTier()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\later-literal", tier: MatchRank.TierName, start: 5, weight: 0.1),
            Candidate(@"C:\earlier-full", tier: MatchRank.TierFull, start: 0, weight: 0.1),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\earlier-full", ranked[0].FullPath);
    }

    // Same start, so weight decides -- again before tier.
    [TestMethod]
    public void RankAndDedupe_HigherWeightBeatsBetterTier()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\weak-literal", tier: MatchRank.TierName, start: 0, weight: 0.1),
            Candidate(@"C:\tight-full", tier: MatchRank.TierFull, start: 0, weight: 0.9),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\tight-full", ranked[0].FullPath);
    }

    // With start and weight equal, tier finally separates: literal > initials > full pinyin.
    [TestMethod]
    public void RankAndDedupe_TierBreaksTiesOnEqualStartAndWeight()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\alias-full", tier: MatchRank.TierFull, start: 0, weight: 1.0),
            Candidate(@"C:\alias-initials", tier: MatchRank.TierInitials, start: 0, weight: 1.0),
            Candidate(@"C:\literal", tier: MatchRank.TierName, start: 0, weight: 1.0),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        CollectionAssert.AreEqual(
            new[] { @"C:\literal", @"C:\alias-initials", @"C:\alias-full" },
            ranked.Select(r => r.FullPath).ToArray());
    }

    [TestMethod]
    public void RankAndDedupe_SameWeight_ShorterNormalizedPathWinsNext()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a\much\longer\path.txt", weight: 0.5, normalizedPath: @"C:\a\much\longer\path.txt"),
            Candidate(@"C:\short.txt", weight: 0.5, normalizedPath: @"C:\short.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\short.txt", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AllTiedExceptPath_SortsAlphabetically()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\zebra.txt", normalizedPath: @"C:\zebra.txt"),
            Candidate(@"C:\apple.txt", normalizedPath: @"C:\apple.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\apple.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\zebra.txt", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_AlphabeticalTiebreakIsCaseInsensitive()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\Bravo.txt", normalizedPath: @"C:\Bravo.txt"),
            Candidate(@"C:\alpha.txt", normalizedPath: @"C:\alpha.txt"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.AreEqual(@"C:\alpha.txt", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_DuplicateNormalizedPath_KeepsOnlyHigherRankedOne()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\dup-weak", isCurated: false, normalizedPath: @"C:\same"),
            Candidate(@"C:\dup-strong", isCurated: true, normalizedPath: @"C:\same"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.HasCount(1, ranked);
        Assert.AreEqual(@"C:\dup-strong", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_DuplicateNormalizedPathIsCaseInsensitive()
    {
        var candidates = new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a", normalizedPath: @"C:\SAME"),
            Candidate(@"C:\b", normalizedPath: @"C:\same"),
        };

        var ranked = SearchResultMapper.RankAndDedupe(candidates);

        Assert.HasCount(1, ranked);
    }

    [TestMethod]
    public void RankAndDedupe_EmptyInput_ReturnsEmptyList() =>
        Assert.IsEmpty(SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>()));

    [TestMethod]
    public void RankAndDedupe_FullPriorityChain_OrdersByEachTierInTurn()
    {
        // curated+lowest-priority should win even though it has the worst weight and longest path.
        var winner = Candidate(@"C:\winner-very-long-path-name.txt", isCurated: true, priority: 0, typeRank: 5, weight: 0.1, normalizedPath: @"C:\winner-very-long-path-name.txt");
        var loser = Candidate(@"C:\z.txt", isCurated: false, priority: 0, typeRank: 0, weight: 1.0, normalizedPath: @"C:\z.txt");

        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate> { loser, winner });

        Assert.AreEqual(winner.Result.FullPath, ranked[0].FullPath);
    }
}
