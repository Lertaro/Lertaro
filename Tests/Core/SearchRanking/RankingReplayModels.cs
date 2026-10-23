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
    ulong EngineTie = 0,
    MatchEvidenceField EvidenceField = MatchEvidenceField.LegacyUnknown,
    MatchQueryVariant QueryVariant = MatchQueryVariant.Original,
    bool ExplicitUserPriority = false,
    int SpanStart = -1,
    int SpanLength = 0,
    MatchEvidenceGrade? ExpectedV3Grade = null)
{
    public bool IsDirectory => (Kind & CandidateKindFlags.Directory) != 0;
    public bool IsApplication => (Kind & CandidateKindFlags.Application) != 0;
    public bool IsShortcut => (Kind & CandidateKindFlags.Shortcut) != 0;

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

    public RankSignals ToV3Signals() => new(
        IsHistoryOnly ? MatchRank.NoMatch : Match,
        IsHistoryOnly,
        IsFavorite,
        ResolveEffectivePriority(),
        AlwaysTop,
        HistoryScoreX100,
        TypeRank,
        IsDirectory,
        DisplayName,
        SyntheticPath.TrimEnd('\\'),
        RankPolicy.DriveRank(Path.GetPathRoot(SyntheticPath)),
        EngineTie);

    public MatchEvidence ToEvidence(string rawQuery)
    {
        if (IsHistoryOnly)
        {
            return new MatchEvidence(
                MatchEvidenceField.HistoryKeyword,
                MatchEvidenceMode.LearnedAlias,
                MatchQueryVariant.HistoryExact,
                MatchEvidenceGrade.LearnedAlias,
                AllPositiveTerms: true,
                DirectText: false);
        }

        var field = EvidenceField != MatchEvidenceField.LegacyUnknown
            ? EvidenceField
            : IsApplication || IsShortcut
                ? MatchEvidenceField.DisplayTitle
                : MatchEvidenceField.FileName;
        var spanStart = SpanStart >= 0 ? SpanStart : Match.Start;
        var spanLength = SpanLength > 0
            ? SpanLength
            : (int)Math.Round(Match.Coverage * DisplayName.Trim().Length, MidpointRounding.AwayFromZero);

        return MatchEvidence.Accepted(
            rawQuery,
            DisplayName,
            field,
            Match,
            MatchEvidenceMode.Literal,
            QueryVariant,
            spanStart: spanStart,
            spanLength: spanLength,
            isDirectory: IsDirectory);
    }

    private IndexRuleEffect ResolveEffectivePriority()
    {
        if (Priority != IndexRuleEffect.Low || ExplicitUserPriority)
            return Priority;

        var normalized = SyntheticPath.TrimEnd('\\');
        return normalized.Contains(@"\Microsoft\Windows\Start Menu\", StringComparison.OrdinalIgnoreCase)
               || ExclusionRuleSet.IsStartMenuRoot(normalized)
            ? IndexRuleEffect.Normal
            : Priority;
    }
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
