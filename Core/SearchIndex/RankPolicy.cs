namespace Lertaro.Core.SearchIndex;

// Shared monotonic ranking kernel (V3). The quick/inline windows sort RankKey arrays via RankKeyComparer;
// the full window decorates SearchResult rows once via SearchResultRanking and sorts via
// RankedSearchResultComparer. Both windows share the section, grade, band, tie-break and tail rules;
// only the quick/inline window carries the configured result-type order:
//
//   Section 0 Pinned       explicit High+AlwaysTop pins first, then strong user Favorites
//   Section 1 Boosted      ordinary High/Normal textual matches (Grade 0..3: ExactIdentity .. SecondaryField)
//   Section 2 Low          explicit Low-priority textual matches (Grade 0..3)
//   Section 3 HistoryTail  pure learned query->path history associations (Grade 4, never jumps ahead of text)

public enum RankSection
{
    Pinned = 0,
    Boosted = 1,
    Low = 2,
    HistoryTail = 3,
}

public enum HistoryTier
{
    None = 0,
    Tail = 1,
    H3 = 2,
    H5 = 3,
}

public readonly record struct RankSignals(
    MatchRank Match,
    bool IsHistoryOnly,
    bool IsFavorite,
    IndexRuleEffect Priority,
    bool AlwaysTop,
    int HistoryScoreX100,
    int TypeRank,
    bool IsDir,
    string Name,
    string NormalizedPath,
    int Drive,
    ulong EngineTie = 0,
    bool TypeRankApplies = true,
    // False when the caller knows nothing outranks the file bucket in the user's result-type order: an
    // exact-named directory then keeps its ExactIdentity (see RankPolicy.GradeForRanking).
    bool DemoteDirectoryIdentity = true);

public readonly record struct RankKey(
    byte AlwaysTop,
    byte Section,
    int Score,
    int HistoryTie,
    int TypeRank,
    MatchRank Match,
    bool IsDir,
    string Name,
    string NormalizedPath,
    int Drive,
    ulong EngineTie,
    byte FavoritePin = 0,
    MatchEvidenceGrade Grade = MatchEvidenceGrade.DirectStrong,
    bool IsFavorite = false,
    IndexRuleEffect Priority = IndexRuleEffect.Normal,
    HistoryTier Tier = HistoryTier.None,
    bool TypeRankApplies = true)
{
    public RankTail Tail => new(IsDir, Name, NormalizedPath, Drive, EngineTie);
    // The configured result-type order is a final tie-break, not a group: section, grade, priority,
    // history band and match quality all run first. It only compares the three built-in buckets
    // (Applications, System settings, Files); an unlisted provider's rank is neutral.
    public int EffectiveTypeRank => TypeRank;
}

public static class RankPolicy
{
    /// <summary>Monotonic evidence-first ranking model version.</summary>
    public const int Version = 3;

    public const int HistoryFiveThresholdX100 = 500;
    public const int HistoryThreeThresholdX100 = 300;
    public const int HistoryFiveMax = 3;
    public const int HistoryThreeMax = 10;

    public static bool IsPreferenceEligible(MatchEvidenceGrade grade) =>
        grade is MatchEvidenceGrade.ExactIdentity or MatchEvidenceGrade.DirectStrong;

    public static int PriorityOrder(IndexRuleEffect priority) => priority switch
    {
        IndexRuleEffect.High => 0,
        IndexRuleEffect.Normal => 1,
        _ => 2,
    };

    public static RankSection Section(
        IndexRuleEffect priority,
        bool explicitAlwaysTop,
        bool favoritePin,
        bool isHistoryOnly,
        MatchEvidenceGrade grade)
    {
        if (explicitAlwaysTop || favoritePin)
            return RankSection.Pinned;
        if (isHistoryOnly || grade == MatchEvidenceGrade.LearnedAlias)
            return RankSection.HistoryTail;
        if (priority == IndexRuleEffect.Low)
            return RankSection.Low;
        return RankSection.Boosted;
    }

    public static RankSection Section(in RankSignals s, HistoryTier tier, in MatchEvidence evidence = default)
    {
        var grade = GradeForRanking(s, evidence);
        var explicitAlwaysTop = s.AlwaysTop && s.Priority == IndexRuleEffect.High;
        var favoritePin = !explicitAlwaysTop && s.IsFavorite && !s.IsHistoryOnly && IsPreferenceEligible(grade);
        return Section(s.Priority, explicitAlwaysTop, favoritePin, s.IsHistoryOnly, grade);
    }

    /// <summary>
    /// Diagnostic ordinal score for trace schema compatibility. Ordering compares structured fields
    /// directly via <see cref="RankKeyComparer"/> rather than cardinal score exchange rates.
    /// </summary>
    public static int DiagnosticScore(
        RankSection section,
        bool explicitAlwaysTop,
        bool favoritePin,
        MatchEvidenceGrade grade,
        IndexRuleEffect priority,
        HistoryTier tier) => section switch
    {
        RankSection.Pinned when explicitAlwaysTop => 500 - (int)grade * 10,
        RankSection.Pinned when favoritePin => 400 - (int)grade * 10,
        RankSection.Boosted => 300 - (int)grade * 20 + (priority == IndexRuleEffect.High ? 2 : 0) + (int)tier,
        RankSection.Low => 100 - (int)grade * 10 + (int)tier,
        _ => 10 + (int)tier,
    };

    public static HistoryTier EffectiveHistoryTier(
        HistoryTier tier,
        MatchEvidenceGrade grade,
        bool isHistoryOnly)
    {
        if (isHistoryOnly || grade == MatchEvidenceGrade.LearnedAlias)
            return tier;
        return IsPreferenceEligible(grade) && tier is HistoryTier.H5 or HistoryTier.H3
            ? tier
            : HistoryTier.None;
    }

    public static int HistoryTie(
        bool isHistoryOnly,
        HistoryTier tier,
        int historyScoreX100,
        MatchEvidenceGrade grade = MatchEvidenceGrade.DirectStrong)
    {
        if (isHistoryOnly || grade == MatchEvidenceGrade.LearnedAlias)
            return tier is HistoryTier.H5 or HistoryTier.H3 or HistoryTier.Tail ? historyScoreX100 : 0;
        return IsPreferenceEligible(grade) && tier is HistoryTier.H5 or HistoryTier.H3
            ? historyScoreX100
            : 0;
    }

    public static RankKey BuildKey(in RankSignals s, HistoryTier tier, in MatchEvidence evidence = default)
    {
        var grade = GradeForRanking(s, evidence);
        var explicitAlwaysTop = s.AlwaysTop && s.Priority == IndexRuleEffect.High;
        var favoritePin = !explicitAlwaysTop && s.IsFavorite && !s.IsHistoryOnly && IsPreferenceEligible(grade);
        var section = Section(s.Priority, explicitAlwaysTop, favoritePin, s.IsHistoryOnly, grade);
        var effectiveTier = EffectiveHistoryTier(tier, grade, s.IsHistoryOnly);
        var historyTie = HistoryTie(s.IsHistoryOnly, effectiveTier, s.HistoryScoreX100, grade);
        var diagScore = DiagnosticScore(section, explicitAlwaysTop, favoritePin, grade, s.Priority, effectiveTier);

        return new RankKey(
            explicitAlwaysTop ? (byte)1 : (byte)0,
            (byte)section,
            diagScore,
            historyTie,
            s.TypeRank,
            s.IsHistoryOnly ? MatchRank.NoMatch : s.Match,
            s.IsDir,
            s.Name,
            s.NormalizedPath,
            s.Drive,
            s.EngineTie,
            favoritePin ? (byte)1 : (byte)0,
            grade,
            s.IsFavorite,
            s.Priority,
            effectiveTier,
            s.TypeRankApplies);
    }

    // Directories are containers, not identities: an exact folder name is a strong direct match but never
    // an ExactIdentity -- as long as the user ranks the file bucket behind another type, so that the
    // configured type order (Applications before Files) can put an app ahead of an unrelated folder with the
    // same leading word. Once Files is the top of that order the cap has nothing left to protect, and
    // demoting would instead strip the strongest evidence from the bucket the user ranked first: a settings
    // row that only matched through a keyword would outrank every exact-named folder while the file bucket
    // still led the order. Callers turn the cap off for exactly that case (RankContext.DemoteDirectoryIdentity).
    private static MatchEvidenceGrade GradeForRanking(in RankSignals s, in MatchEvidence evidence)
    {
        var strong = !s.IsHistoryOnly && s.Match.IsStrong;
        var grade = MatchEvidence.ResolveGrade(evidence, s.Match, s.IsHistoryOnly, strong);
        return s.IsDir && s.DemoteDirectoryIdentity && grade == MatchEvidenceGrade.ExactIdentity
            ? MatchEvidenceGrade.DirectStrong
            : grade;
    }

    /// <summary>
    /// Assigns capped H5/H3 tiers by decayed score. Only history-only or strong filename matches qualify;
    /// remaining history-only rows become Tail. Candidate enumeration order does not affect the result.
    /// </summary>
    public static IReadOnlyDictionary<string, HistoryTier> BuildHistoryBudget(
        IEnumerable<RankSignals> candidates,
        int fiveMax = HistoryFiveMax,
        int threeMax = HistoryThreeMax) =>
        BuildHistoryBudget(candidates, tierEligiblePaths: null, fiveMax, threeMax);

    /// <summary>
    /// Same budget, but only rows on <paramref name="tierEligiblePaths"/> may spend an H5/H3 slot.
    /// A Grade 2/3 row cannot read a lifted tier (<see cref="EffectiveHistoryTier"/> drops it), so it
    /// must not spend a slot whose lift it will never use. History-only rows always qualify.
    /// </summary>
    public static IReadOnlyDictionary<string, HistoryTier> BuildHistoryBudget(
        IEnumerable<RankSignals> candidates,
        IReadOnlySet<string>? tierEligiblePaths,
        int fiveMax = HistoryFiveMax,
        int threeMax = HistoryThreeMax)
    {
        fiveMax = Math.Max(0, fiveMax);
        threeMax = Math.Max(0, threeMax);
        var tiers = new Dictionary<string, HistoryTier>(StringComparer.OrdinalIgnoreCase);
        var qualified = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var historyOnlyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in candidates)
        {
            var path = s.NormalizedPath;
            if (s.IsHistoryOnly)
            {
                historyOnlyPaths.Add(path);
            }
            else if (tierEligiblePaths != null && !tierEligiblePaths.Contains(path))
            {
                // The row stays in the candidate set, but it cannot spend a history slot: its grade
                // never reads the tier, so letting it qualify would only mislabel the trace and badge
                // (or, on a tied score, take a band from a row that can use it).
                continue;
            }

            if (s.HistoryScoreX100 < HistoryThreeThresholdX100)
                continue;
            if (!s.IsHistoryOnly && !s.Match.IsStrong)
                continue;

            if (!qualified.TryGetValue(path, out var best) || s.HistoryScoreX100 > best)
                qualified[path] = s.HistoryScoreX100;
        }

        var ordered = new List<(string Path, int Score)>(qualified.Count);
        foreach (var (path, score) in qualified)
            ordered.Add((path, score));
        ordered.Sort(static (a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Path, b.Path);
        });

        var fiveTaken = 0;
        var threeTaken = 0;
        foreach (var (path, score) in ordered)
        {
            if (score >= HistoryFiveThresholdX100 && fiveTaken < fiveMax)
            {
                fiveTaken++;
                Assign(tiers, path, HistoryTier.H5);
            }
            else if (threeTaken < threeMax)
            {
                threeTaken++;
                Assign(tiers, path, HistoryTier.H3);
            }
        }

        foreach (var path in historyOnlyPaths)
            Assign(tiers, path, HistoryTier.Tail);

        return tiers;
    }

    private static void Assign(Dictionary<string, HistoryTier> tiers, string path, HistoryTier tier)
    {
        if (!tiers.TryGetValue(path, out var existing) || tier > existing)
            tiers[path] = tier;
    }

    public static int CompareCommonTail(in RankTail x, in RankTail y)
    {
        var compare = x.IsDir.CompareTo(y.IsDir);
        if (compare != 0)
            return compare;

        compare = SearchNameOrderComparer.Instance.Compare(x.Name, y.Name);
        if (compare != 0)
            return compare;

        compare = x.Name.Length.CompareTo(y.Name.Length);
        if (compare != 0)
            return compare;

        compare = x.NormalizedPath.Length.CompareTo(y.NormalizedPath.Length);
        if (compare != 0)
            return compare;

        compare = x.Drive.CompareTo(y.Drive);
        if (compare != 0)
            return compare;

        compare = string.Compare(x.NormalizedPath, y.NormalizedPath, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        compare = string.CompareOrdinal(x.NormalizedPath, y.NormalizedPath);
        if (compare != 0)
            return compare;

        return x.EngineTie.CompareTo(y.EngineTie);
    }

    public static int DriveRank(string? drive) => string.IsNullOrEmpty(drive) ? 0 : char.ToUpperInvariant(drive[0]);
}

public readonly record struct RankTail(bool IsDir, string Name, string NormalizedPath, int Drive, ulong EngineTie);

public sealed class RankKeyComparer : IComparer<RankKey>
{
    public static readonly RankKeyComparer Instance = new();

    public int Compare(RankKey x, RankKey y)
    {
        var c = x.Section.CompareTo(y.Section);
        if (c != 0)
            return c;

        switch ((RankSection)x.Section)
        {
            case RankSection.Pinned:
                c = y.AlwaysTop.CompareTo(x.AlwaysTop);
                if (c != 0)
                    return c;
                c = y.FavoritePin.CompareTo(x.FavoritePin);
                if (c != 0)
                    return c;
                c = x.Grade.CompareTo(y.Grade);
                if (c != 0)
                    return c;
                c = RankPolicy.PriorityOrder(x.Priority).CompareTo(RankPolicy.PriorityOrder(y.Priority));
                if (c != 0)
                    return c;
                c = y.Tier.CompareTo(x.Tier);
                if (c != 0)
                    return c;
                c = y.HistoryTie.CompareTo(x.HistoryTie);
                if (c != 0)
                    return c;
                c = MatchRank.CompareQuality(x.Match, y.Match);
                if (c != 0)
                    return c;
                c = CompareTypeRank(x, y);
                return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);

            case RankSection.Low:
                c = x.Grade.CompareTo(y.Grade);
                if (c != 0)
                    return c;
                c = y.Tier.CompareTo(x.Tier);
                if (c != 0)
                    return c;
                c = y.HistoryTie.CompareTo(x.HistoryTie);
                if (c != 0)
                    return c;
                c = MatchRank.CompareQuality(x.Match, y.Match);
                if (c != 0)
                    return c;
                c = CompareTypeRank(x, y);
                return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);

            case RankSection.HistoryTail:
                c = y.Tier.CompareTo(x.Tier);
                if (c != 0)
                    return c;
                c = y.HistoryTie.CompareTo(x.HistoryTie);
                if (c != 0)
                    return c;
                c = CompareTypeRank(x, y);
                return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);

            default:
                c = x.Grade.CompareTo(y.Grade);
                if (c != 0)
                    return c;
                if (x.IsFavorite != y.IsFavorite)
                    return x.IsFavorite ? -1 : 1;
                c = RankPolicy.PriorityOrder(x.Priority).CompareTo(RankPolicy.PriorityOrder(y.Priority));
                if (c != 0)
                    return c;
                c = y.Tier.CompareTo(x.Tier);
                if (c != 0)
                    return c;
                c = y.HistoryTie.CompareTo(x.HistoryTie);
                if (c != 0)
                    return c;
                c = MatchRank.CompareQuality(x.Match, y.Match);
                if (c != 0)
                    return c;
                c = CompareTypeRank(x, y);
                return c != 0 ? c : RankPolicy.CompareCommonTail(x.Tail, y.Tail);
        }
    }

    // Type order is deliberately neutral unless both rows belong to one of the three built-in buckets
    // (Applications / System settings / Files) and every earlier key has already tied.
    private static int CompareTypeRank(in RankKey x, in RankKey y) =>
        x.TypeRankApplies && y.TypeRankApplies ? x.TypeRank.CompareTo(y.TypeRank) : 0;
}
