namespace Lertaro.Core.SearchIndex;

/// <summary>
/// Thin V3 replay surface retained for <c>OfflineRankingPolicies</c> and <c>RankPolicyV3Tests</c>,
/// delegating directly to the active <see cref="RankPolicy"/> (<c>Version = 3</c>) kernel so
/// offline replay gates execute the exact live ranking key and comparer without code duplication.
/// </summary>
public readonly record struct V3RankKey(RankKey Inner)
{
    public byte AlwaysTop => Inner.AlwaysTop;
    public byte Section => Inner.Section;
    public MatchEvidenceGrade Grade => Inner.Grade;
    public int HistoryTie => Inner.HistoryTie;
    public int EffectiveTypeRank => Inner.EffectiveTypeRank;
    public MatchRank Match => Inner.Match;
    public RankTail Tail => Inner.Tail;
    public string NormalizedPath => Inner.NormalizedPath;
}

public static class RankPolicyV3
{
    public const int Version = RankPolicy.Version;

    public static V3RankKey BuildKey(
        RankSignals signals,
        HistoryTier tier,
        MatchEvidence evidence = default) =>
        new(RankPolicy.BuildKey(signals, tier, evidence));
}

public sealed class V3RankKeyComparer : IComparer<V3RankKey>
{
    public static readonly V3RankKeyComparer Instance = new();

    public int Compare(V3RankKey x, V3RankKey y) =>
        RankKeyComparer.Instance.Compare(x.Inner, y.Inner);
}
