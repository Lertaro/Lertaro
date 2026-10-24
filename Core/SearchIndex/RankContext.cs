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
        string? query = null,
        bool demoteDirectoryIdentity = true)
    {
        PriorityResolver = priorityResolver;
        HistoryScoreX100 = historyScoreX100 ?? NoScores;
        FavoritePaths = favoritePaths;
        HistoryFiveMax = Math.Max(0, historyFiveMax);
        HistoryThreeMax = Math.Max(0, historyThreeMax);
        Query = query;
        DemoteDirectoryIdentity = demoteDirectoryIdentity;
    }

    public Func<string, bool, PriorityVerdict>? PriorityResolver { get; }
    public IReadOnlyDictionary<string, int> HistoryScoreX100 { get; }
    public IReadOnlySet<string>? FavoritePaths { get; }
    public int HistoryFiveMax { get; }
    public int HistoryThreeMax { get; }

    /// <summary>Clean query string used by full-window decoration to detect ExactIdentity stem matches.</summary>
    public string? Query { get; }

    /// <summary>
    /// Whether an exact-named directory is capped at DirectStrong. True (today's behaviour) unless the
    /// caller established that the file bucket is the user's top-ranked type; see RankPolicy.GradeForRanking.
    /// </summary>
    public bool DemoteDirectoryIdentity { get; }
}
