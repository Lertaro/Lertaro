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

    /// <summary>The biggest bonus the first type in the user's result-type order earns on a strong match.</summary>
    public const int TypeBonusMax = 16;

    /// <summary>
    /// The quick window's score: the base C/P score plus the row's display-type weight. Only the base score
    /// is weighted -- a favorite in the fifth type must stay above a strong match in the first, and the
    /// history tiers are global by the same argument. The full window has no type layer and keeps calling
    /// <see cref="Score(in RankSignals, HistoryTier)"/> through <c>SearchResultRanking.Decorate</c>.
    /// </summary>
    public static int QuickScore(in RankSignals s, HistoryTier tier) =>
        UsesBaseScore(s.Match.IsStrong, s.IsFavorite, s.IsHistoryOnly, tier)
            ? Score(s, tier) + TypeBonus(s.TypeRank, s.Match)
            : Score(s, tier);

    /// <summary>The rows whose score the display-type order is allowed to weight.</summary>
    public static bool UsesBaseScore(bool isStrong, bool isFavorite, bool isHistoryOnly, HistoryTier tier) =>
        !(isFavorite && isStrong)
        && tier is not (HistoryTier.H5 or HistoryTier.H3)
        && !(isHistoryOnly && tier == HistoryTier.Tail);

    /// <summary>
    /// How much the user's result-type order adds to a row's score. The first type gets enough to lift a
    /// reasonable match over a perfect match two or three types down, and the bonus decays per position
    /// instead of acting as a hard group. A weak match keeps only its own quality share of the bonus
    /// (<see cref="MatchRank.Weight"/>), so a barely matching row in the first type still loses to a strong
    /// match further down. Unlisted types (int.MaxValue) and any type past the fourth get nothing.
    /// </summary>
    public static int TypeBonus(int typeRank, in MatchRank match)
    {
        var max = typeRank switch
        {
            0 => TypeBonusMax,
            1 => TypeBonusMax / 2,
            2 => TypeBonusMax / 4,
            3 => TypeBonusMax / 16,
            _ => 0,
        };
        if (max == 0 || !match.IsMatch)
            return 0;
        if (match.IsStrong)
            return max;
        return (int)Math.Round(max * Math.Clamp(match.Weight, 0, 1), MidpointRounding.AwayFromZero);
    }

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
    /// The history key, on a 0..1000 scale: how much the user actually uses this row, as the store already
    /// decayed it.
    /// </summary>
    /// <remarks>
    /// It used to hand back the score only for H5/H3 rows and zero for everything else, which made the
    /// budget's thirteen slots decide whether a row's own history counted at all. A name opened every day
    /// but ranked eleventh in some other query fell to zero here and then lost on coverage to any short
    /// name -- the history was recorded, displayed ("常用"), and never reached the ordering. The budget
    /// still shapes the sections and the tail; it no longer gates the key.
    ///
    /// A row with no history scores 0 here, so this key is neutral between rows the user never touched and
    /// matching decides between them.
    /// </remarks>
    public static int HistoryTie(bool isHistoryOnly, HistoryTier tier, int historyScoreX100) =>
        historyScoreX100;

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

            // Fork: the left edge, then coverage, then the type order -- the same sequence every other
            // section uses, so a history-tail row is ordered by its match, not by its type alone.
            var byStartTail = x.Start.CompareTo(y.Start);
            if (byStartTail != 0)
                return byStartTail;

            var byCoverageTail = y.Coverage.CompareTo(x.Coverage);
            if (byCoverageTail != 0)
                return byCoverageTail;

            return x.TypeRank.CompareTo(y.TypeRank);
        }

        // Match quality is one layer, and it is the layer that decides order: where the query sits in the
        // name, then how tightly it sits there, then how much of the name it fills.
        //
        // The left edge leads. A name whose query lands at its first character is what the user meant, and no
        // amount of usage or type preference changes that -- a row used every day must not outrank a row
        // whose name simply starts with what was typed.
        var byStart = x.Start.CompareTo(y.Start);
        if (byStart != 0)
            return byStart;

        var byContiguity = y.Contiguity.CompareTo(x.Contiguity);
        if (byContiguity != 0)
            return byContiguity;

        // Usage next, as a band: among rows that matched equally well, one the user opens is ahead of one
        // they never touch.
        //
        // Low is excluded. That section exists because the user explicitly demoted the path, and an explicit
        // instruction outranks an implicit one: letting history lead there would silently undo the setting
        // for exactly the paths someone bothered to mark.
        if (section != RankSection.Low)
        {
            var byHistory = y.HistoryTie.CompareTo(x.HistoryTie);
            if (byHistory != 0)
                return byHistory;
        }

        // The configured result-type order, ahead of coverage.
        //
        // Coverage divides the matched length by the NAME's length, so a short name fills a larger share of
        // itself and beats a longer one no matter what type the user ranked first: a three-letter query
        // against a ten-letter application name scores 0.3 while against a five-letter file name it scores
        // 0.6, purely because the file name is shorter. Reading the type order first is what lets "I put
        // applications first" mean something when the names differ in length.
        //
        // No section exemption. History and other implicit signals are dropped in Low, but the type order is
        // an explicit setting and applies wherever two rows of different types compete.
        var byType = x.TypeRank.CompareTo(y.TypeRank);
        if (byType != 0)
            return byType;

        // How much of the name the query fills, then the strong/weak flag. Both windows read these here so
        // they cannot disagree.
        var byCoverage = y.Coverage.CompareTo(x.Coverage);
        if (byCoverage != 0)
            return byCoverage;

        var byScore = y.Score.CompareTo(x.Score);
        if (byScore != 0)
            return byScore;

        // Low stops here: match quality is the caller's to compare, and TypeRank was already settled above.
        if (section == RankSection.Low)
            return 0;

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
/// <param name="Start">Leftmost matched character; the first key inside a section (0 for rows with no name match).</param>
/// <param name="Contiguity">How tightly the matched characters sit together, 0..1; the second key.</param>
/// <param name="Coverage">How much of the name the query fills, 0..1; the third key.</param>
public readonly record struct SectionRank(
    int Score,
    int HistoryTie,
    int TypeRank,
    int Start = 0,
    double Coverage = 0,
    double Contiguity = 0);

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
        var rankX = new SectionRank(x.Score, x.HistoryTie, x.TypeRank, x.Match.Start, x.Match.Coverage, x.Match.Contiguity);
        var rankY = new SectionRank(y.Score, y.HistoryTie, y.TypeRank, y.Match.Start, y.Match.Coverage, y.Match.Contiguity);

        compare = RankPolicy.CompareSectionRank(section, in rankX, in rankY);
        if (compare != 0)
            return compare;

        compare = MatchRank.CompareQuality(x.Match, y.Match);
        if (compare != 0)
            return compare;

        // TypeRank is compared inside CompareSectionRank for every section, Low included, so it is already
        // settled by the time this point is reached.
        return RankPolicy.CompareCommonTail(x.Tail, y.Tail);
    }
}
