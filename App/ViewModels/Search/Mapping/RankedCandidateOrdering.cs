using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Quick-window adapter for RankPolicy. It merges paths before assigning history tiers so duplicate sources
// share one row. Scores and comparison order remain in Core/SearchIndex.
internal static class RankedCandidateOrdering
{
    private const string ApplicationKind = "Application";

    public static List<SearchResultMapper.RankedCandidate> Order(
        List<SearchResultMapper.RankedCandidate> candidates,
        RankContext? context = null) =>
        OrderCore(candidates, context, captureTrace: false, out _);

    /// <summary>Orders normally and retains the computed keys for final-page diagnostics.</summary>
    internal static List<SearchResultMapper.RankedCandidate> OrderWithTrace(
        List<SearchResultMapper.RankedCandidate> candidates,
        RankContext? context,
        out RankingPassTrace trace) =>
        OrderCore(candidates, context, captureTrace: true, out trace);

    private static List<SearchResultMapper.RankedCandidate> OrderCore(
        List<SearchResultMapper.RankedCandidate> candidates,
        RankContext? context,
        bool captureTrace,
        out RankingPassTrace trace)
    {
        if (candidates.Count == 0)
        {
            trace = captureTrace
                ? new RankingPassTrace(
                    0,
                    Array.Empty<SearchResultMapper.RankedCandidate>(),
                    Array.Empty<RankKey>(),
                    context,
                    Array.Empty<SearchResultMapper.RankedCandidate>())
                : RankingPassTrace.Empty;
            return [];
        }

        List<SearchResultMapper.RankedCandidate>? mergedAway = captureTrace ? new() : null;
        var rows = MergeByPath(candidates, mergedAway);
        var signals = new RankSignals[rows.Length];
        for (var i = 0; i < rows.Length; i++)
            signals[i] = ToSignals(rows[i], context);

        var tierEligiblePaths = BuildTierEligiblePaths(rows, signals);
        var budget = RankPolicy.BuildHistoryBudget(
            signals,
            tierEligiblePaths,
            context?.HistoryFiveMax ?? RankPolicy.HistoryFiveMax,
            context?.HistoryThreeMax ?? RankPolicy.HistoryThreeMax);
        var keys = new RankKey[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var tier = budget.GetValueOrDefault(signals[i].NormalizedPath, HistoryTier.None);
            keys[i] = RankPolicy.BuildKey(signals[i], tier, rows[i].Evidence);
            // The "frequent" badge: only the two lifted history tiers are a habit. A history-tail row
            // (fewer than three effective opens) is merely known, so it gets no badge.
            rows[i].Result.IsFrequent = tier >= HistoryTier.H3;
        }

        Array.Sort(keys, rows, RankKeyComparer.Instance);
        trace = captureTrace
            ? new RankingPassTrace(candidates.Count, rows, keys, context, mergedAway ?? [])
            : RankingPassTrace.Empty;
        return [.. rows];
    }

    /// <summary>
    /// Keeps the display cap from swallowing a whole result type: before the list is truncated, each type
    /// keeps its best <paramref name="perType"/> rows and the rest of the slots go back to the global order.
    /// The relative order of the kept rows never changes -- this only decides which rows survive. Types the
    /// user never listed share the int.MaxValue rank and therefore share one slot group.
    /// </summary>
    internal static List<SearchResultMapper.RankedCandidate> EnsureTypeCoverage(
        List<SearchResultMapper.RankedCandidate> ordered,
        int cap,
        int perType = 3)
    {
        if (ordered.Count <= cap || cap <= 0 || perType <= 0)
            return ordered;

        var take = new bool[ordered.Count];
        var seenPerType = new Dictionary<int, int>();
        var taken = 0;
        for (var i = 0; i < ordered.Count && taken < cap; i++)
        {
            var type = ordered[i].TypeRank;
            seenPerType.TryGetValue(type, out var seen);
            if (seen >= perType)
                continue;
            seenPerType[type] = seen + 1;
            take[i] = true;
            taken++;
        }

        for (var i = 0; i < ordered.Count && taken < cap; i++)
        {
            if (take[i])
                continue;
            take[i] = true;
            taken++;
        }

        var kept = new List<SearchResultMapper.RankedCandidate>(taken);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (take[i])
                kept.Add(ordered[i]);
        }
        return kept;
    }

    internal static RankingLogRow Describe(
        SearchResultMapper.RankedCandidate candidate,
        RankKey key,
        int kernelRank,
        RankContext? context,
        RankingProviderState providerState = RankingProviderState.Unknown)
    {
        var signal = ToSignals(candidate, context);
        var isApplication = IsApplication(candidate);
        var identity = CandidateIdentity.Create(
            candidate.Source,
            candidate.Result.IsDir,
            isApplication,
            candidate.Result.Name,
            candidate.NormalizedPath);
        return new RankingLogRow(
            candidate.NormalizedPath,
            kernelRank,
            (RankSection)key.Section,
            key.Score,
            key.TypeRank,
            signal.Priority,
            // The effective tier, not the raw budget entry: a Grade 2/3 row never reads the tier, so
            // reporting the budget's label here would explain an order the key did not produce.
            key.Tier,
            key.HistoryTie,
            signal.Match,
            signal.IsFavorite,
            signal.IsHistoryOnly,
            signal.IsDir,
            isApplication,
            candidate.Source,
            candidate.Result.SourceProvider?.GetType().Name,
            providerState,
            candidate.Evidence,
            identity);
    }

    /// <summary>
    /// Describes an alias-recalled row without admitting it to the production merge, history budget or
    /// ordering pass. Offline replay can compare the complete signals; the synthetic key is intentionally
    /// marked kernel rank -1 because no displayed rank was assigned.
    /// </summary>
    internal static RankingLogRow DescribeShadow(
        SearchResultMapper.RankedCandidate candidate,
        RankContext? context,
        RankingProviderState providerState)
    {
        var signals = ToSignals(candidate, context);
        const HistoryTier tier = HistoryTier.None;
        var key = RankPolicy.BuildKey(signals, tier, candidate.Evidence);
        return Describe(
            candidate,
            key,
            -1,
            context,
            providerState);
    }

    // A lifted history tier only affects ranks it is allowed to lift (see MatchEvidenceGrade). Computing
    // that before the budget keeps a secondary-field alias from spending an H5 slot a Grade 0/1 row
    // would have used, since the alias can never read the lift.
    private static HashSet<string> BuildTierEligiblePaths(
        SearchResultMapper.RankedCandidate[] rows,
        RankSignals[] signals)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Length; i++)
        {
            var signal = signals[i];
            if (signal.IsHistoryOnly)
            {
                paths.Add(signal.NormalizedPath);
                continue;
            }

            var grade = MatchEvidence.ResolveGrade(
                rows[i].Evidence,
                signal.Match,
                isHistoryOnly: false,
                isStrongMatch: signal.Match.IsStrong);
            if (RankPolicy.IsPreferenceEligible(grade))
                paths.Add(signal.NormalizedPath);
        }
        return paths;
    }

    private static SearchResultMapper.RankedCandidate[] MergeByPath(
        List<SearchResultMapper.RankedCandidate> candidates,
        List<SearchResultMapper.RankedCandidate>? mergedAway)
    {
        var merged = new List<SearchResultMapper.RankedCandidate>(candidates.Count);
        var slotByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var key = PathKey(candidate);
            if (!slotByPath.TryGetValue(key, out var slot))
            {
                slotByPath[key] = merged.Count;
                merged.Add(candidate);
                continue;
            }

            var previous = merged[slot];
            var combined = PreferRow(previous, candidate);
            mergedAway?.Add(ReferenceEquals(combined.Result, candidate.Result) ? previous : candidate);
            merged[slot] = combined;
        }

        return [.. merged];
    }

    // Application views own duplicate paths for execution; favorite, history and source signals combine.
    private static SearchResultMapper.RankedCandidate PreferRow(
        SearchResultMapper.RankedCandidate a,
        SearchResultMapper.RankedCandidate b)
    {
        var better = CompareRows(a, b);
        var winner = better <= 0 ? a : b;
        var textOwner = PreferTextEvidence(a, b);

        return winner with
        {
            // Application views continue to own execution and presentation, while every path-level signal
            // survives a duplicate file/application merge.
            IsFavorite = a.IsFavorite || b.IsFavorite,
            HistoryCount = Math.Max(a.HistoryCount, b.HistoryCount),
            TypeRank = Math.Min(a.TypeRank, b.TypeRank),
            Match = textOwner.Match,
            // Any direct source makes the merged path a direct result.
            IsHistoryOnly = a.IsHistoryOnly && b.IsHistoryOnly,
            Source = a.Source | b.Source,
            Evidence = textOwner.Evidence,
        };
    }

    private static SearchResultMapper.RankedCandidate PreferTextEvidence(
        SearchResultMapper.RankedCandidate a,
        SearchResultMapper.RankedCandidate b)
    {
        // A learned history lookup must not lend its alias quality to a path that also has direct text.
        if (a.IsHistoryOnly != b.IsHistoryOnly)
            return a.IsHistoryOnly ? b : a;

        var quality = MatchRank.CompareQuality(a.Match, b.Match);
        if (quality != 0)
            return quality < 0 ? a : b;

        var evidence = CompareEvidence(a.Evidence, b.Evidence);
        return evidence <= 0 ? a : b;
    }

    private static int CompareEvidence(MatchEvidence a, MatchEvidence b)
    {
        static int Grade(MatchEvidenceGrade value) =>
            value == MatchEvidenceGrade.LegacyUnknown ? int.MaxValue : (int)value;
        static int Known<T>(T value) where T : struct, Enum =>
            Convert.ToInt32(value) == 0 ? int.MaxValue : Convert.ToInt32(value);

        var compare = Grade(a.Grade).CompareTo(Grade(b.Grade));
        if (compare != 0)
            return compare;
        compare = Known(a.Field).CompareTo(Known(b.Field));
        if (compare != 0)
            return compare;
        compare = Known(a.Mode).CompareTo(Known(b.Mode));
        if (compare != 0)
            return compare;
        compare = Known(a.QueryVariant).CompareTo(Known(b.QueryVariant));
        if (compare != 0)
            return compare;
        compare = string.Compare(a.ProviderId, b.ProviderId, StringComparison.Ordinal);
        if (compare != 0)
            return compare;
        var aStart = a.SpanStart < 0 ? int.MaxValue : a.SpanStart;
        var bStart = b.SpanStart < 0 ? int.MaxValue : b.SpanStart;
        compare = aStart.CompareTo(bStart);
        return compare != 0 ? compare : b.SpanLength.CompareTo(a.SpanLength);
    }

    private static int CompareRows(
        SearchResultMapper.RankedCandidate a,
        SearchResultMapper.RankedCandidate b)
    {
        var byKind = (IsApplication(a) ? 0 : 1).CompareTo(IsApplication(b) ? 0 : 1);
        if (byKind != 0)
            return byKind;

        var byFavorite = (a.IsFavorite ? 0 : 1).CompareTo(b.IsFavorite ? 0 : 1);
        if (byFavorite != 0)
            return byFavorite;

        var byHistory = (a.IsHistoryOnly ? 1 : 0).CompareTo(b.IsHistoryOnly ? 1 : 0);
        if (byHistory != 0)
            return byHistory;

        var byQuality = MatchRank.CompareQuality(a.Match, b.Match);
        if (byQuality != 0)
            return byQuality;

        var byType = a.TypeRank.CompareTo(b.TypeRank);
        if (byType != 0)
            return byType;

        // Fully tied rows keep the one that arrived first, so the merge never depends on enumeration order.
        return SearchNameOrderComparer.Instance.Compare(a.Result.Name, b.Result.Name);
    }

    private static bool IsApplication(SearchResultMapper.RankedCandidate candidate) =>
        string.Equals(candidate.Result.ResultKind, ApplicationKind, StringComparison.Ordinal);

    private static string PathKey(SearchResultMapper.RankedCandidate candidate) =>
        candidate.NormalizedPath.TrimEnd('\\');

    private static RankSignals ToSignals(SearchResultMapper.RankedCandidate c, RankContext? context)
    {
        var path = PathKey(c);
        var verdict = context?.PriorityResolver?.Invoke(path, c.Result.IsDir)
                      ?? new PriorityVerdict(IndexRuleEffect.Normal, false);

        // The snapshot is authoritative when it has the path, including a decayed-to-zero entry: falling
        // back to the raw open count there would resurrect a score the decay already retired. Only rows the
        // caller never scored (tests, Windows Recent) derive from HistoryCount.
        var historyScore = context is not null && context.HistoryScoreX100.TryGetValue(path, out var known)
            ? known
            : Math.Min(10, Math.Max(0, c.HistoryCount)) * 100;

        return new RankSignals(
            Match: c.IsHistoryOnly ? MatchRank.NoMatch : c.Match,
            IsHistoryOnly: c.IsHistoryOnly,
            IsFavorite: c.IsFavorite,
            Priority: verdict.Effect,
            AlwaysTop: verdict.AlwaysTop,
            HistoryScoreX100: historyScore,
            TypeRank: c.TypeRank,
            IsDir: c.Result.IsDir,
            Name: c.Result.Name,
            NormalizedPath: path,
            Drive: RankPolicy.DriveRank(c.Result.Drive),
            TypeRankApplies: c.TypeRankApplies,
            DemoteDirectoryIdentity: context?.DemoteDirectoryIdentity ?? true);
    }
}
