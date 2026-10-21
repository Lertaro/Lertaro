using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Everything one query resolves once and hands to the kernel: the index-rule verdict per path, the
/// decayed history score per path, and the favorite paths. Favorites are handed over as a set because
/// only the full window's index rows have to discover them; the quick window reads
/// <c>RankedCandidate.IsFavorite</c> instead and never looks at this field, so there is no second source
/// of truth for a row that already carries the flag.
/// </summary>
public sealed class RankContext
{
    private static readonly IReadOnlyDictionary<string, int> NoScores = new Dictionary<string, int>();

    public RankContext(
        Func<string, bool, PriorityVerdict>? priorityResolver,
        IReadOnlyDictionary<string, int>? historyScoreX100 = null,
        IReadOnlySet<string>? favoritePaths = null,
        int historyFiveMax = RankPolicy.HistoryFiveMax,
        int historyThreeMax = RankPolicy.HistoryThreeMax)
    {
        PriorityResolver = priorityResolver;
        HistoryScoreX100 = historyScoreX100 ?? NoScores;
        FavoritePaths = favoritePaths;
        HistoryFiveMax = Math.Max(0, historyFiveMax);
        HistoryThreeMax = Math.Max(0, historyThreeMax);
    }

    public Func<string, bool, PriorityVerdict>? PriorityResolver { get; }

    /// <summary>Effective history score times 100, keyed by normalized path; empty when the caller has none.</summary>
    public IReadOnlyDictionary<string, int> HistoryScoreX100 { get; }

    /// <summary>Normalized paths the user starred; null when the caller has no favorites to hand over.</summary>
    public IReadOnlySet<string>? FavoritePaths { get; }

    /// <summary>
    /// How many rows may reach H5 / H3. Only the quick-window path reads them, through
    /// <see cref="RankPolicy.BuildHistoryBudget"/>; the full window derives its tier from the thresholds
    /// alone and never consults a budget, so these say nothing there. The inline card passes 1 and 3 --
    /// it shows a fraction of the rows the quick window does.
    /// </summary>
    public int HistoryFiveMax { get; }

    public int HistoryThreeMax { get; }
}
