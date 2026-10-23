using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class HistoryBudgetTests
{
    private static RankSignals Signals(string path, int scoreX100, bool strong = true, bool historyOnly = false) =>
        new(strong
                ? new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWholeWord)
                : new MatchRank(MatchRank.TierName, 0, 0.3, 0.5, MatchRank.ShapeScattered),
            historyOnly, false, IndexRuleEffect.Normal, false, scoreX100, 0, false, "name", path, 3);

    private static HistoryTier TierOf(IReadOnlyDictionary<string, HistoryTier> budget, string path) =>
        budget.TryGetValue(path, out var tier) ? tier : HistoryTier.None;

    [TestMethod]
    public void ThreeFivePlusRows_AreAllH5()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\a.txt", 600),
            Signals(@"D:\h\b.txt", 520),
            Signals(@"D:\h\c.txt", 500),
        });

        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\b.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\c.txt"));
    }

    [TestMethod]
    public void FourthFivePlusRow_FallsToH3()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\a.txt", 600),
            Signals(@"D:\h\b.txt", 550),
            Signals(@"D:\h\c.txt", 520),
            Signals(@"D:\h\d.txt", 500),
        });

        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\b.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\c.txt"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\h\d.txt"));
    }

    [TestMethod]
    public void TwoFivePlusAndOneFourNine_DoNotFillH5()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\five.txt", 600),
            Signals(@"D:\h\near.txt", 510),
            Signals(@"D:\h\under.txt", 490),
        });

        // The cap is not a quota: a spare H5 slot stays empty instead of pulling up a 4.9 row.
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\five.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\near.txt"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\h\under.txt"));
    }

    [TestMethod]
    public void H3_CapsAtTen()
    {
        var candidates = new List<RankSignals>();
        for (var i = 0; i < 3; i++)
            candidates.Add(Signals($@"D:\h\five{i}.txt", 600));
        for (var i = 0; i < 12; i++)
            candidates.Add(Signals($@"D:\h\three{i}.txt", 400));

        var budget = RankPolicy.BuildHistoryBudget(candidates);

        Assert.HasCount(13, budget);
        Assert.AreEqual(3, budget.Count(kv => kv.Value == HistoryTier.H5));
        Assert.AreEqual(10, budget.Count(kv => kv.Value == HistoryTier.H3));
        // The two left out are indexed rows, so they get no bucket at all rather than a Tail entry.
        Assert.AreEqual(13, candidates.Count(c => budget.ContainsKey(c.NormalizedPath)));
    }

    [TestMethod]
    public void IneligiblePath_DoesNotSpendASlot()
    {
        // A Grade 2/3 row can never read an H5/H3 tier, so the budget must not hand it one: the second
        // row is the only path allowed to spend the single H5 slot.
        var eligible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\h\b.txt" };

        var budget = RankPolicy.BuildHistoryBudget(
            new[]
            {
                Signals(@"D:\h\a.txt", 900),
                Signals(@"D:\h\b.txt", 800),
            },
            eligible,
            fiveMax: 1,
            threeMax: 10);

        Assert.IsFalse(budget.ContainsKey(@"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\b.txt"));
    }

    [TestMethod]
    public void FourteenthHistoryOnlyRow_FallsToTail()
    {
        var candidates = new List<RankSignals>();
        for (var i = 0; i < 3; i++)
            candidates.Add(Signals($@"D:\h\five{i}.txt", 600, historyOnly: true));
        for (var i = 0; i < 10; i++)
            candidates.Add(Signals($@"D:\h\three{i}.txt", 400, historyOnly: true));
        candidates.Add(Signals(@"D:\h\last.txt", 350, historyOnly: true));

        var budget = RankPolicy.BuildHistoryBudget(candidates);

        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\last.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\five0.txt"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\h\three0.txt"));
    }

    [TestMethod]
    public void LowScoreIndexedRow_IsAbsent()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[] { Signals(@"D:\h\weak.txt", 250) });

        Assert.IsFalse(budget.ContainsKey(@"D:\h\weak.txt"));
    }

    [TestMethod]
    public void LowScoreHistoryOnlyRow_IsTail()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\one.txt", 250, historyOnly: true),
        });

        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\one.txt"));
    }

    [TestMethod]
    public void WeakIndexedRow_IsAbsentEvenWithHighScore()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\scattered.txt", 900, strong: false),
        });

        Assert.IsFalse(budget.ContainsKey(@"D:\h\scattered.txt"));
    }

    [TestMethod]
    public void WeakHistoryOnlyRow_StillGetsH5()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\scattered.txt", 900, strong: false, historyOnly: true),
        });

        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\scattered.txt"));
    }

    [TestMethod]
    public void TiedScores_BreakByPathOrdinal()
    {
        var budget = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\d", 500),
            Signals(@"D:\c", 500),
            Signals(@"D:\b", 500),
            Signals(@"D:\a", 500),
        });

        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\a"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\b"));
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\c"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\d"));
    }

    [TestMethod]
    public void DuplicatePath_KeepsHighestTier()
    {
        var lowThenHigh = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\same.txt", 320),
            Signals(@"D:\h\same.txt", 600),
        });
        var highThenLow = RankPolicy.BuildHistoryBudget(new[]
        {
            Signals(@"D:\h\same.txt", 600),
            Signals(@"D:\h\same.txt", 320),
        });

        Assert.AreEqual(HistoryTier.H5, TierOf(lowThenHigh, @"D:\h\same.txt"));
        Assert.AreEqual(HistoryTier.H5, TierOf(highThenLow, @"D:\h\same.txt"));
        Assert.HasCount(1, lowThenHigh);
    }

    [TestMethod]
    public void EmptyCandidates_ReturnsEmpty() =>
        Assert.IsEmpty(RankPolicy.BuildHistoryBudget(Array.Empty<RankSignals>()));

    [TestMethod]
    public void Caps_FiveMaxOfOne_LeavesTheSecondFiveInThree()
    {
        var budget = RankPolicy.BuildHistoryBudget(
            new[]
            {
                Signals(@"D:\h\a.txt", 1000, historyOnly: true),
                Signals(@"D:\h\b.txt", 900, historyOnly: true),
                Signals(@"D:\h\c.txt", 800, historyOnly: true),
            },
            fiveMax: 1,
            threeMax: 10);

        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\h\b.txt"));
        Assert.AreEqual(HistoryTier.H3, TierOf(budget, @"D:\h\c.txt"));
    }

    [TestMethod]
    public void Caps_ThreeMaxOfZero_LeavesOnlyFive()
    {
        var budget = RankPolicy.BuildHistoryBudget(
            new[]
            {
                Signals(@"D:\h\a.txt", 1000, historyOnly: true),
                Signals(@"D:\h\b.txt", 900, historyOnly: true),
                Signals(@"D:\h\c.txt", 800, historyOnly: true),
            },
            fiveMax: 1,
            threeMax: 0);

        // A history row with no H3 slot left is not unlifted: it lands in the tail like any other
        // history-only row, so it still sits after the low-priority section.
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\b.txt"));
        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\c.txt"));
    }

    [TestMethod]
    public void Caps_ThreeMaxOfZero_DropsIndexedStrongOverflow()
    {
        var budget = RankPolicy.BuildHistoryBudget(
            new[]
            {
                Signals(@"D:\h\a.txt", 1000),
                Signals(@"D:\h\b.txt", 900),
                Signals(@"D:\h\c.txt", 800),
            },
            fiveMax: 1,
            threeMax: 0);

        // An indexed row never gets a Tail entry: outside H5/H3 it keeps its plain C/P score.
        Assert.HasCount(1, budget);
        Assert.AreEqual(HistoryTier.H5, TierOf(budget, @"D:\h\a.txt"));
        Assert.IsFalse(budget.ContainsKey(@"D:\h\b.txt"));
        Assert.IsFalse(budget.ContainsKey(@"D:\h\c.txt"));
    }

    [TestMethod]
    public void Caps_NegativeValues_AreTreatedAsZero()
    {
        var budget = RankPolicy.BuildHistoryBudget(
            new[]
            {
                Signals(@"D:\h\a.txt", 1000, historyOnly: true),
                Signals(@"D:\h\b.txt", 900, historyOnly: true),
                Signals(@"D:\h\c.txt", 800, historyOnly: true),
            },
            fiveMax: -1,
            threeMax: -1);

        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\a.txt"));
        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\b.txt"));
        Assert.AreEqual(HistoryTier.Tail, TierOf(budget, @"D:\h\c.txt"));
    }
}
