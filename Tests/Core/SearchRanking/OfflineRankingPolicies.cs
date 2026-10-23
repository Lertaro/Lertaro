using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

/// <summary>
/// Self-contained frozen V2 reference evaluator so historical baseline diffs remain bit-for-bit stable even
/// after production switches <see cref="RankPolicy"/> to V3 in P7.
/// </summary>
internal sealed class V2OfflineRankingPolicy : IOfflineRankingPolicy
{
    public static readonly V2OfflineRankingPolicy Instance = new();

    public string Name => "V2/policy-2";

    public RankingRun Rank(RankingCase rankingCase)
    {
        var rows = rankingCase.Candidates
            .Select(candidate => (Candidate: candidate, Signals: candidate.ToV2Signals()))
            .ToArray();
        var budget = RankPolicy.BuildHistoryBudget(rows.Select(row => row.Signals));
        var decorated = new (CandidateSnapshot Candidate, RankKey Key)[rows.Length];

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var tier = budget.GetValueOrDefault(row.Signals.NormalizedPath, HistoryTier.None);
            decorated[i] = (row.Candidate, BuildFrozenV2Key(row.Signals, tier));
        }

        Array.Sort(decorated, static (left, right) => CompareFrozenV2(left.Key, right.Key));
        return new RankingRun(Name, decorated
            .Select((row, index) => new RankedSnapshot(row.Candidate, row.Key, index + 1))
            .ToArray());
    }

    internal static RankKey BuildFrozenV2Key(in RankSignals s, HistoryTier tier)
    {
        var strong = !s.IsHistoryOnly && s.Match.IsStrong;
        var pinned = s.AlwaysTop && s.Priority == IndexRuleEffect.High;
        var section = pinned
            ? RankSection.Pinned
            : s.IsHistoryOnly && tier is not (HistoryTier.H5 or HistoryTier.H3)
                ? RankSection.HistoryTail
                : (s.IsFavorite && strong) || tier is HistoryTier.H5 or HistoryTier.H3
                    ? RankSection.Boosted
                    : s.Priority == IndexRuleEffect.Low
                        ? RankSection.Low
                        : RankSection.Boosted;

        var baseScore = s.IsHistoryOnly
            ? 0
            : s.Priority switch
            {
                IndexRuleEffect.High => strong ? 26 : 16,
                IndexRuleEffect.Normal => strong ? 18 : 8,
                _ => strong ? 10 : 0,
            };

        var typeBonus = 0;
        if (s.Match.IsMatch)
        {
            var baseBonus = s.TypeRank switch
            {
                0 => 16,
                1 => 8,
                2 => 4,
                3 => 1,
                _ => 0,
            };
            if (baseBonus > 0)
            {
                var quality = s.Match.Tier <= MatchRank.TierFull
                    ? 1d
                    : Math.Clamp(0.25d + 0.75d * s.Match.Coverage * s.Match.Contiguity, 0d, 1d);
                typeBonus = (int)Math.Round(baseBonus * quality, MidpointRounding.AwayFromZero);
            }
        }

        var quickScore = baseScore + typeBonus;
        if (s.IsFavorite && strong)
            quickScore = Math.Max(quickScore, 44);
        if (tier == HistoryTier.H5)
            quickScore = Math.Max(quickScore, 34);
        else if (tier == HistoryTier.H3)
            quickScore = Math.Max(quickScore, 22);
        else if (tier == HistoryTier.Tail)
            quickScore = Math.Max(quickScore, 6);

        var historyTie = tier is HistoryTier.H5 or HistoryTier.H3 or HistoryTier.Tail ? s.HistoryScoreX100 : 0;
        return new RankKey(
            pinned ? (byte)1 : (byte)0,
            (byte)section,
            quickScore,
            historyTie,
            s.TypeRank,
            s.IsHistoryOnly ? MatchRank.NoMatch : s.Match,
            s.IsDir,
            s.Name,
            s.NormalizedPath,
            s.Drive,
            s.EngineTie);
    }

    internal static int CompareFrozenV2(RankKey x, RankKey y)
    {
        var c = x.Section.CompareTo(y.Section);
        if (c != 0)
            return c;
        c = y.Score.CompareTo(x.Score);
        if (c != 0)
            return c;
        if (x.Section == (byte)RankSection.Low)
        {
            c = MatchRank.CompareQuality(x.Match, y.Match);
            if (c != 0)
                return c;
            c = x.TypeRank.CompareTo(y.TypeRank);
            return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);
        }
        if (x.Section == (byte)RankSection.Boosted || x.Section == (byte)RankSection.HistoryTail)
        {
            c = y.HistoryTie.CompareTo(x.HistoryTie);
            if (c != 0)
                return c;
        }
        c = x.TypeRank.CompareTo(y.TypeRank);
        if (c != 0)
            return c;
        c = MatchRank.CompareQuality(x.Match, y.Match);
        return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);
    }
}

internal sealed class V3OfflineRankingPolicy : IOfflineRankingPolicy
{
    public static readonly V3OfflineRankingPolicy Instance = new();

    public string Name => $"V3/policy-{RankPolicyV3.Version}";

    public RankingRun Rank(RankingCase rankingCase)
    {
        var rows = rankingCase.Candidates
            .Select(candidate => (
                Candidate: candidate,
                Signals: candidate.ToV3Signals(),
                Evidence: candidate.ToEvidence(rankingCase.RawQuery)))
            .ToArray();
        // Same eligibility rule as the live quick window: a row whose Grade never reads an H5/H3 tier
        // cannot spend one of the budget's slots.
        var tierEligiblePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Signals.IsHistoryOnly)
            {
                tierEligiblePaths.Add(row.Signals.NormalizedPath);
                continue;
            }

            var grade = MatchEvidence.ResolveGrade(
                row.Evidence,
                row.Signals.Match,
                isHistoryOnly: false,
                isStrongMatch: row.Signals.Match.IsStrong);
            if (RankPolicy.IsPreferenceEligible(grade))
                tierEligiblePaths.Add(row.Signals.NormalizedPath);
        }
        var budget = RankPolicy.BuildHistoryBudget(rows.Select(row => row.Signals), tierEligiblePaths);
        var decorated = new (CandidateSnapshot Candidate, V3RankKey V3Key, RankKeyCompat Key)[rows.Length];

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var tier = budget.GetValueOrDefault(row.Signals.NormalizedPath, HistoryTier.None);
            var v3 = RankPolicyV3.BuildKey(row.Signals, tier, row.Evidence);
            var projection = new RankKey(
                v3.AlwaysTop,
                v3.Section,
                -(int)v3.Grade,
                v3.HistoryTie,
                v3.EffectiveTypeRank,
                v3.Match,
                v3.Tail.IsDir,
                v3.Tail.Name,
                v3.Tail.NormalizedPath,
                v3.Tail.Drive,
                v3.Tail.EngineTie);
            decorated[i] = (row.Candidate, v3, new RankKeyCompat(projection));
        }

        Array.Sort(decorated, static (left, right) => V3RankKeyComparer.Instance.Compare(left.V3Key, right.V3Key));
        return new RankingRun(Name, decorated
            .Select((row, index) => new RankedSnapshot(row.Candidate, row.Key.Value, index + 1))
            .ToArray());
    }

    private readonly record struct RankKeyCompat(RankKey Value);
}

internal sealed class DelegateOfflineRankingPolicy(
    string name,
    Func<RankingCase, IReadOnlyList<(CandidateSnapshot Candidate, RankKey Key)>> rank)
    : IOfflineRankingPolicy
{
    public string Name { get; } = name;

    public RankingRun Rank(RankingCase rankingCase) => new(Name, rank(rankingCase)
        .Select((row, index) => new RankedSnapshot(row.Candidate, row.Key, index + 1))
        .ToArray());
}

internal static class RankingReplay
{
    public static RankingComparison Compare(
        RankingCase rankingCase,
        IOfflineRankingPolicy baseline,
        IOfflineRankingPolicy candidate) =>
        new(baseline.Rank(rankingCase), candidate.Rank(rankingCase));
}
