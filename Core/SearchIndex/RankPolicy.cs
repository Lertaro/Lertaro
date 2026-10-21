namespace Lertaro.Core.SearchIndex;

// The ranking kernel both search windows share: the score of one candidate, which section it lands in,
// and the key order inside that section. Design, numbers and rationale:
// dev-docs/search-ranking-redesign.md sections 2-5. Both windows rank through this kernel: the quick one
// via RankKeyComparer, the full one via SearchResultRanking + RankedSearchResultComparer.
//
//   0 Pinned       the "always top" exit of a high-priority index rule
//   1 Boosted      favorite strong matches, history H5/H3, and every normal or high priority row
//   2 Low          demoted by an index rule and not lifted by anything above
//   3 HistoryTail  a history-only row whose weighted score is below 3
//
// Pinned and Boosted rank by the exchangeable score S = 10*C + 8*P, so one priority step buys 0.8 of a
// match step and a high-priority weak match (16) still lands below a normal-priority strong one (18).
// Low and HistoryTail stay separate sections because no weighted sum can put the low-priority bucket
// (10/0) above a weak history row (6) and keep the six documented tiers in order at the same time.

/// <summary>The four ranking buckets, in display order. See the header above.</summary>
public enum RankSection
{
    Pinned = 0,
    Boosted = 1,
    Low = 2,
    HistoryTail = 3,
}

/// <summary>
/// Which history bucket a row was assigned by the caller's budget (redesign 3.4). Tail means a history
/// row whose effective score is under 3; None means no history. The budget itself is not here yet -- the
/// caller decides the tier and passes it in.
/// </summary>
public enum HistoryTier
{
    None = 0,
    Tail = 1,
    H3 = 2,
    H5 = 3,
}

/// <summary>Everything the kernel needs about one candidate, already resolved by the caller.</summary>
/// <param name="HistoryScoreX100">Effective history score times 100, so the tie-break keeps 0.01 detail.</param>
/// <param name="TypeRank">Display type order (quick window only); the full window passes 0.</param>
/// <param name="NormalizedPath">Path with any trailing separator removed; the caller normalizes once.</param>
/// <param name="EngineTie">The engine's own low bits, consulted only after every window-level key.</param>
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
    ulong EngineTie = 0);

/// <summary>A candidate after decoration: the three computed numbers plus every key the comparer reads.</summary>
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
    ulong EngineTie)
{
    /// <summary>The shared tail keys, read straight off this row so the comparer builds no second object.</summary>
    public RankTail Tail => new(IsDir, Name, NormalizedPath, Drive, EngineTie);
}

/// <summary>Scores and sections of redesign 3.2. The constants are the only tunable numbers in the kernel.</summary>
public static class RankPolicy
{
    /// <summary>
    /// The ranking model's version, written into the calibration log so rows collected under different
    /// policies can be told apart afterwards. Bump it with any change to the score, the sections or the key
    /// order. It is a code constant, not a setting: a policy version that a user could edit measures nothing.
    /// </summary>
    public const int Version = 2;

    public const int StrongWeight = 10;
    public const int PriorityWeight = 8;
    public const int FavoriteStrongScore = 44;
    public const int HistoryFiveScore = 34;
    public const int HistoryThreeScore = 22;
    public const int HistoryTailScore = 6;

    // History buckets (redesign 3.4). The thresholds decide the tier; the caps only truncate, so a fourth
    // 5+ row drops into H3 rather than out of the boosted rows. Scores arrive as X100 from
    // SearchHistoryStore.ScoreSnapshot(now), already decayed -- the budget must not decay twice.
    public const int HistoryFiveThresholdX100 = 500;
    public const int HistoryThreeThresholdX100 = 300;
    public const int HistoryFiveMax = 3;
    public const int HistoryThreeMax = 10;

    /// <summary>
    /// The score of one candidate, from the facts the kernel reads. A favorite only earns its bonus on a
    /// strong match; history tiers come from the caller's budget, and only a history-only row is scored as
    /// the tail, so an indexed row with one or two opens keeps its C/P. The full window passes
    /// <paramref name="isHistoryOnly"/> false: it never adds history rows.
    /// </summary>
    public static int Score(
        bool isStrong,
        bool isFavorite,
        bool isHistoryOnly,
        IndexRuleEffect priority,
        HistoryTier tier)
    {
        if (isFavorite && isStrong)
            return FavoriteStrongScore;
        if (tier == HistoryTier.H5)
            return HistoryFiveScore;
        if (tier == HistoryTier.H3)
            return HistoryThreeScore;
        if (isHistoryOnly && tier == HistoryTier.Tail)
            return HistoryTailScore;

        var c = isStrong ? 1 : 0;
        var p = priority switch
        {
            IndexRuleEffect.High => 2,
            IndexRuleEffect.Normal => 1,
            _ => 0,
        };
        return StrongWeight * c + PriorityWeight * p;
    }

    public static int Score(in RankSignals s, HistoryTier tier) =>
        Score(s.Match.IsStrong, s.IsFavorite, s.IsHistoryOnly, s.Priority, tier);

    /// <summary>
    /// The section of one candidate, in the fixed order of redesign 3.2: always-top first, then anything
    /// history or a favorite lifts (regardless of its priority bucket -- a low-priority H5 row is Boosted),
    /// then a weak history-only row, then the low-priority bucket. A pin is only ever honored on a High
    /// bucket, which is what the caller's verdict already guarantees.
    /// </summary>
    public static RankSection Section(
        bool isStrong,
        bool isFavorite,
        bool isHistoryOnly,
        IndexRuleEffect priority,
        bool alwaysTop,
        HistoryTier tier)
    {
        if (alwaysTop && priority == IndexRuleEffect.High)
            return RankSection.Pinned;
        if ((isFavorite && isStrong) || tier is HistoryTier.H5 or HistoryTier.H3)
            return RankSection.Boosted;
        if (isHistoryOnly && tier == HistoryTier.Tail)
            return RankSection.HistoryTail;
        if (priority == IndexRuleEffect.Low)
            return RankSection.Low;
        return RankSection.Boosted;
    }

    public static RankSection Section(in RankSignals s, HistoryTier tier) =>
        Section(s.Match.IsStrong, s.IsFavorite, s.IsHistoryOnly, s.Priority, s.AlwaysTop, tier);

    /// <summary>
    /// The in-bucket history order, precise to 0.01. H5 and H3 rows break a score tie by their history
    /// score; only a history-only row carries one at Tail, so an indexed row sitting at Tail (Score still
    /// counts its C/P) and a row without history are both zero and stay tied.
    /// </summary>
    public static int HistoryTie(bool isHistoryOnly, HistoryTier tier, int historyScoreX100) => tier switch
    {
        HistoryTier.H5 or HistoryTier.H3 => historyScoreX100,
        HistoryTier.Tail when isHistoryOnly => historyScoreX100,
        _ => 0,
    };

    public static int HistoryTie(in RankSignals s, HistoryTier tier) =>
        HistoryTie(s.IsHistoryOnly, tier, s.HistoryScoreX100);

    /// <summary>Decorates signals once so the comparer never recomputes a key, resolves a dictionary or trims a path.</summary>
    public static RankKey BuildKey(in RankSignals s, HistoryTier tier) => new(
        (byte)(s.AlwaysTop && s.Priority == IndexRuleEffect.High ? 1 : 0),
        (byte)Section(s, tier),
        Score(s, tier),
        HistoryTie(s, tier),
        s.TypeRank,
        s.Match,
        s.IsDir,
        s.Name,
        s.NormalizedPath,
        s.Drive,
        s.EngineTie);

    /// <summary>
    /// Splits candidates into the history buckets of redesign 3.4. The thresholds decide which tier a row
    /// gets and the caps only truncate the overflow, so the fourth 5+ row lands in H3 instead of dropping
    /// to the tail, and a 3-5 row never fills a spare H5 slot. Only a history-only row or a strong
    /// filename match qualifies, and every pure history row that did not make H5/H3 lands at Tail.
    /// Paths missing from the result are <see cref="HistoryTier.None"/>. The order of the candidates
    /// cannot change the outcome: rows are ranked by score, then by the path's ordinal order.
    /// </summary>
    /// <param name="fiveMax">Rows allowed into H5; a 5+ row past the cap keeps its lift by falling through
    /// to H3 (see <paramref name="threeMax"/>). The inline card passes 1 instead of the default 3.</param>
    /// <param name="threeMax">Rows allowed into H3. At zero the only lifted rows are the H5 ones, and every
    /// other pure history row falls to <see cref="HistoryTier.Tail"/>.</param>
    public static IReadOnlyDictionary<string, HistoryTier> BuildHistoryBudget(
        IEnumerable<RankSignals> candidates,
        int fiveMax = HistoryFiveMax,
        int threeMax = HistoryThreeMax)
    {
        fiveMax = Math.Max(0, fiveMax);
        threeMax = Math.Max(0, threeMax);
        var tiers = new Dictionary<string, HistoryTier>(StringComparer.OrdinalIgnoreCase);
        // One entry per path, carrying its best score, so a duplicate cannot spend two of the caps.
        var qualified = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var historyOnlyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in candidates)
        {
            var path = s.NormalizedPath;
            if (s.IsHistoryOnly)
                historyOnlyPaths.Add(path);

            if (s.HistoryScoreX100 < HistoryThreeThresholdX100)
                continue;
            // A weak filename match earns no lift from history alone: the row has to be a history row or
            // one the query genuinely explains.
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

    // Strongest tier wins. HistoryTier is ordered None < Tail < H3 < H5, so a path reached twice keeps the
    // better bucket instead of whatever the last pass wrote.
    private static void Assign(Dictionary<string, HistoryTier> tiers, string path, HistoryTier tier)
    {
        if (!tiers.TryGetValue(path, out var existing) || tier > existing)
            tiers[path] = tier;
    }

    /// <summary>
    /// The keys every section compares before it reaches match quality, so both windows share one rule.
    /// TypeRank is 0 in the full window, which has no display type layer; the low-priority section's
    /// TypeRank is NOT compared here -- see CompareSectionRank.
    /// </summary>
    public static int CompareSectionRank(RankSection section, in SectionRank x, in SectionRank y)
    {
        if (section == RankSection.HistoryTail)
        {
            var byHistory = y.HistoryTie.CompareTo(x.HistoryTie);
            if (byHistory != 0)
                return byHistory;

            return x.TypeRank.CompareTo(y.TypeRank);
        }

        var byScore = y.Score.CompareTo(x.Score);
        if (byScore != 0)
            return byScore;

        // Low is the one section that reads match quality before the display type order, and quality is
        // the caller's to compare, so this stops here and leaves TypeRank to it.
        if (section == RankSection.Low)
            return 0;

        if (section == RankSection.Boosted)
        {
            var byHistory = y.HistoryTie.CompareTo(x.HistoryTie);
            if (byHistory != 0)
                return byHistory;
        }

        return x.TypeRank.CompareTo(y.TypeRank);
    }

    /// <summary>The keys every section shares, in the order both windows have always used.</summary>
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

        // Case-only differences are still distinct rows; sorting by Ordinal keeps the order total.
        compare = string.CompareOrdinal(x.NormalizedPath, y.NormalizedPath);
        if (compare != 0)
            return compare;

        return x.EngineTie.CompareTo(y.EngineTie);
    }

    /// <summary>Rank order for a drive spelled as its letter ("C"), or for the empty string a row with no
    /// filesystem drive carries. The character's ordinal is what a case-insensitive string comparison of
    /// those same spellings produced, so no row moves.</summary>
    public static int DriveRank(string? drive) => string.IsNullOrEmpty(drive) ? 0 : char.ToUpperInvariant(drive[0]);
}

/// <summary>Per-section ordering inputs. See RankPolicy.CompareSectionRank for what each section reads.</summary>
public readonly record struct SectionRank(int Score, int HistoryTie, int TypeRank);

/// <summary>The tail every section shares, already reduced to values a comparer can read.</summary>
public readonly record struct RankTail(bool IsDir, string Name, string NormalizedPath, int Drive, ulong EngineTie);

/// <summary>
/// The key order of redesign 3.3. Sections decide first; inside a section the sequence differs on
/// purpose, because the display type order is an explicit user preference that outranks quality in every
/// bucket except Low, where the owner asked for quality first.
/// </summary>
public sealed class RankKeyComparer : IComparer<RankKey>
{
    public static readonly RankKeyComparer Instance = new();

    public int Compare(RankKey x, RankKey y)
    {
        var compare = x.Section.CompareTo(y.Section);
        if (compare != 0)
            return compare;

        var section = (RankSection)x.Section;
        var rankX = new SectionRank(x.Score, x.HistoryTie, x.TypeRank);
        var rankY = new SectionRank(y.Score, y.HistoryTie, y.TypeRank);

        compare = RankPolicy.CompareSectionRank(section, in rankX, in rankY);
        if (compare != 0)
            return compare;

        compare = MatchRank.CompareQuality(x.Match, y.Match);
        if (compare != 0)
            return compare;

        // Only the low-priority bucket places the display type order after quality; every other section
        // consumed TypeRank inside CompareSectionRank.
        if (section == RankSection.Low)
        {
            compare = x.TypeRank.CompareTo(y.TypeRank);
            if (compare != 0)
                return compare;
        }

        return RankPolicy.CompareCommonTail(x.Tail, y.Tail);
    }
}
