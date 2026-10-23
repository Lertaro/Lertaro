using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

internal sealed class V2OfflineRankingPolicy : IOfflineRankingPolicy
{
    public static readonly V2OfflineRankingPolicy Instance = new();

    public string Name => $"V2/policy-{RankPolicy.Version}";

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
            decorated[i] = (row.Candidate, RankPolicy.BuildKey(row.Signals, tier) with
            {
                Score = RankPolicy.QuickScore(row.Signals, tier),
            });
        }

        Array.Sort(decorated, static (left, right) => RankKeyComparer.Instance.Compare(left.Key, right.Key));
        return new RankingRun(Name, decorated
            .Select((row, index) => new RankedSnapshot(row.Candidate, row.Key, index + 1))
            .ToArray());
    }
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
