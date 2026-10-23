using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

[Flags]
internal enum CandidateKindFlags
{
    None = 0,
    File = 1,
    Directory = 2,
    Application = 4,
    Shortcut = 8,
}

internal enum CandidateSource
{
    FileIndex,
    SearchableProvider,
    Favorite,
    History,
    Recent,
    Plugin,
}

internal sealed record CandidateSnapshot(
    string Token,
    string DisplayName,
    string SyntheticPath,
    CandidateKindFlags Kind,
    CandidateSource Source,
    MatchRank Match,
    IndexRuleEffect Priority,
    bool AlwaysTop,
    int HistoryScoreX100,
    int TypeRank,
    int Relevance,
    bool IsFavorite = false,
    bool IsHistoryOnly = false,
    ulong EngineTie = 0)
{
    public bool IsDirectory => (Kind & CandidateKindFlags.Directory) != 0;

    public RankSignals ToV2Signals() => new(
        IsHistoryOnly ? MatchRank.NoMatch : Match,
        IsHistoryOnly,
        IsFavorite,
        Priority,
        AlwaysTop,
        HistoryScoreX100,
        TypeRank,
        IsDirectory,
        DisplayName,
        SyntheticPath.TrimEnd('\\'),
        RankPolicy.DriveRank(Path.GetPathRoot(SyntheticPath)),
        EngineTie);
}

internal sealed record ExpectedPair(string Before, string After);

internal sealed record TopKExpectation(string Token, int K);

internal sealed record RankingCase(
    string Id,
    string Window,
    string RawQuery,
    bool FuzzyEnabled,
    IReadOnlyList<string> TypeOrder,
    string ProviderReadiness,
    IReadOnlyList<CandidateSnapshot> Candidates,
    IReadOnlyList<ExpectedPair> MustAppearBefore,
    IReadOnlyList<TopKExpectation> MustBeTopK,
    IReadOnlySet<string> MustBeAbsent)
{
    public IReadOnlySet<string> RelevantTokens => Candidates
        .Where(candidate => candidate.Relevance > 0)
        .Select(candidate => candidate.Token)
        .ToHashSet(StringComparer.Ordinal);
}

internal sealed record RankedSnapshot(CandidateSnapshot Candidate, RankKey Key, int Rank);

internal sealed record RankingRun(string Policy, IReadOnlyList<RankedSnapshot> Rows)
{
    public int RankOf(string token)
    {
        var row = Rows.FirstOrDefault(row => string.Equals(row.Candidate.Token, token, StringComparison.Ordinal));
        return row?.Rank ?? 0;
    }

    public IReadOnlyList<string> Tokens => Rows.Select(row => row.Candidate.Token).ToArray();
}

internal interface IOfflineRankingPolicy
{
    string Name { get; }
    RankingRun Rank(RankingCase rankingCase);
}

internal sealed record RankingComparison(RankingRun Baseline, RankingRun Candidate)
{
    public int TargetRankDelta(string token) => RankingMetrics.TargetRankDelta(Baseline, Candidate, token);
}
