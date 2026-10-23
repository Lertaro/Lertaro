using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Per-query ranking inputs shared across quick, inline and full search windows.
/// </summary>
public sealed class RankContext
{
    private static readonly IReadOnlyDictionary<string, int> NoScores = new Dictionary<string, int>();

    public RankContext(
        Func<string, bool, PriorityVerdict>? priorityResolver,
        IReadOnlyDictionary<string, int>? historyScoreX100 = null,
        IReadOnlySet<string>? favoritePaths = null,
        int historyFiveMax = RankPolicy.HistoryFiveMax,
        int historyThreeMax = RankPolicy.HistoryThreeMax,
        string? query = null)
    {
        PriorityResolver = priorityResolver;
        HistoryScoreX100 = historyScoreX100 ?? NoScores;
        FavoritePaths = favoritePaths;
        HistoryFiveMax = Math.Max(0, historyFiveMax);
        HistoryThreeMax = Math.Max(0, historyThreeMax);
        Query = query;
    }

    public Func<string, bool, PriorityVerdict>? PriorityResolver { get; }
    public IReadOnlyDictionary<string, int> HistoryScoreX100 { get; }
    public IReadOnlySet<string>? FavoritePaths { get; }
    public int HistoryFiveMax { get; }
    public int HistoryThreeMax { get; }

    /// <summary>Clean query string used by full-window decoration to detect ExactIdentity stem matches.</summary>
    public string? Query { get; }
}
