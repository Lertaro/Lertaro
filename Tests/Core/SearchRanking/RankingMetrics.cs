namespace Lertaro.Core.Tests.SearchRanking;

internal static class RankingMetrics
{
    internal readonly record struct RunMetrics(
        double NdcgAt10,
        int PairwiseInversions,
        int MissingExpectedTokens);

    public static RunMetrics Evaluate(RankingCase rankingCase, RankingRun run) =>
        new(
            NdcgAt(run, 10),
            PairwiseInversions(run, rankingCase.MustAppearBefore),
            MissingExpectedTokens(run, rankingCase.MustAppearBefore));

    public static double RecallAt(RankingRun run, IReadOnlySet<string> relevantTokens, int k)
    {
        if (relevantTokens.Count == 0)
            return 1;
        if (k <= 0)
            return 0;

        var found = run.Rows.Take(k).Count(row => relevantTokens.Contains(row.Candidate.Token));
        return (double)found / relevantTokens.Count;
    }

    public static double ReciprocalRank(RankingRun run, IReadOnlySet<string> relevantTokens)
    {
        var first = run.Rows.FirstOrDefault(row => relevantTokens.Contains(row.Candidate.Token));
        return first == null ? 0 : 1d / first.Rank;
    }

    public static double NdcgAt(RankingRun run, int k)
    {
        if (k <= 0)
            return 0;

        var actual = Dcg(run.Rows.Take(k).Select(row => row.Candidate.Relevance));
        var ideal = Dcg(run.Rows
            .Select(row => row.Candidate.Relevance)
            .OrderDescending()
            .Take(k));
        return ideal == 0 ? 1 : actual / ideal;
    }

    // Order failures only: a pair whose tokens both reached the run but landed the wrong way round.
    public static int PairwiseInversions(RankingRun run, IEnumerable<ExpectedPair> expectedPairs)
    {
        var ranks = run.Rows.ToDictionary(row => row.Candidate.Token, row => row.Rank, StringComparer.Ordinal);
        var inversions = 0;
        foreach (var pair in expectedPairs)
        {
            if (ranks.TryGetValue(pair.Before, out var before)
                && ranks.TryGetValue(pair.After, out var after)
                && before >= after)
            {
                inversions++;
            }
        }
        return inversions;
    }

    // A token that never reached the run is a recall failure, not an ordering one: counted as an inversion it
    // would look like the kernel misordered two rows when one of them was simply absent.
    public static int MissingExpectedTokens(RankingRun run, IEnumerable<ExpectedPair> expectedPairs)
    {
        var ranks = run.Rows.ToDictionary(row => row.Candidate.Token, row => row.Rank, StringComparer.Ordinal);
        return expectedPairs
            .Where(pair => !ranks.ContainsKey(pair.Before) || !ranks.ContainsKey(pair.After))
            .Count();
    }

    public static int TargetRankDelta(RankingRun baseline, RankingRun candidate, string token)
    {
        var before = baseline.RankOf(token);
        var after = candidate.RankOf(token);
        if (before == 0 || after == 0)
            throw new ArgumentException($"Target '{token}' must occur in both runs.", nameof(token));
        return after - before;
    }

    public static bool MeetsAssertions(RankingCase rankingCase, RankingRun run)
    {
        var tokens = run.Tokens;
        if (rankingCase.MustBeAbsent.Any(token => tokens.Contains(token, StringComparer.Ordinal)))
            return false;
        if (rankingCase.MustBeTopK.Any(expectation =>
                run.RankOf(expectation.Token) is var rank && (rank == 0 || rank > expectation.K)))
            return false;
        return PairwiseInversions(run, rankingCase.MustAppearBefore) == 0
            && MissingExpectedTokens(run, rankingCase.MustAppearBefore) == 0;
    }

    private static double Dcg(IEnumerable<int> relevance)
    {
        var rank = 1;
        var total = 0d;
        foreach (var grade in relevance)
        {
            if (grade > 0)
                total += (Math.Pow(2, grade) - 1) / Math.Log2(rank + 1);
            rank++;
        }
        return total;
    }
}
