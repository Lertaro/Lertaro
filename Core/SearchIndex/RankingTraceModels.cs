namespace Lertaro.Core.SearchIndex;

/// <summary>Ranking-kernel fields for one candidate, before final page composition.</summary>
internal readonly record struct RankingLogRow(
    string Path,
    int KernelRank,
    RankSection Section,
    int Score,
    int TypeRank,
    IndexRuleEffect Priority,
    HistoryTier Tier,
    int HistoryTie,
    MatchRank Match,
    bool IsFavorite,
    bool IsHistoryOnly,
    bool IsDir,
    bool IsApplication,
    RankingCandidateSource Source = RankingCandidateSource.Unknown,
    string? ProviderId = null,
    RankingProviderState ProviderState = RankingProviderState.Unknown,
    MatchEvidence Evidence = default,
    CandidateIdentity Identity = default);

/// <summary>Search surface that produced a ranking impression.</summary>
internal enum RankingTraceWindow : byte
{
    Unknown = 0,
    Quick = 1,
    Inline = 2,
    Full = 3,
}

/// <summary>Candidate sources. Duplicate paths combine flags; ordering ignores them.</summary>
[Flags]
internal enum RankingCandidateSource : ushort
{
    Unknown = 0,
    FileIndex = 1 << 0,
    SearchableProvider = 1 << 1,
    Favorite = 1 << 2,
    History = 1 << 3,
    WindowsRecent = 1 << 4,
    InstantProvider = 1 << 5,
    PluginAction = 1 << 6,
    Synthetic = 1 << 7,
}

/// <summary>The cache state observed for a searchable-item provider during this query.</summary>
internal enum RankingProviderState : byte
{
    Unknown = 0,
    NotStarted = 1,
    Loading = 2,
    ReadyEmpty = 3,
    ReadyWithItems = 4,
    Stale = 5,
}

/// <summary>The last stage at which a candidate was present.</summary>
internal enum RankingDropStage : byte
{
    Unknown = 0,
    Displayed = 1,
    QueriedDirectoryFilter = 2,
    ScopeFilter = 3,
    TypeTriggerFilter = 4,
    FolderOnlyFilter = 5,
    MergeDedupe = 6,
    TokenDomainFilter = 7,
    TokenFilter = 8,
    FinalDisplayCap = 9,
    // Recalled by provider metadata, deliberately excluded from the production candidate set.
    ShadowAliasGate = 10,
}

/// <summary>Why a displayed row survived composition; this does not affect its ordering key.</summary>
internal enum RankingSelectionStage : byte
{
    Unknown = 0,
    PluginPrefix = 1,
    KernelOrder = 2,
    TypeCoverage = 3,
    TokenOrder = 4,
}

internal readonly record struct RankingLogSession(
    string SearchSessionId,
    long QuerySequence,
    RankingTraceWindow Window,
    string RawQueryHash,
    string CanonicalQueryHash,
    int QueryVariantFlags);

internal readonly record struct RankingProviderTrace(
    string ProviderId,
    RankingProviderState State,
    int ItemCount,
    int HitCount);

/// <summary>Candidate counts at each retrieval, filter, merge and composition boundary.</summary>
internal readonly record struct RankingPageCounts(
    int EngineCandidateCount,
    int AfterMapperFilterCount,
    int PreMergeCount,
    int PostMergeCount,
    int PrefixCount,
    int TypeCoverageSelectedCount,
    int BeforeFinalCapCount,
    int FinalShownCount,
    int TokenInputCount = 0,
    int TokenOutputCount = 0,
    int FinalCapDroppedCount = 0);

internal readonly record struct RankingDisplayedRow(
    string Path,
    int FinalDisplayedRank,
    RankingCandidateSource Source,
    RankingSelectionStage SelectionStage,
    string? ProviderId,
    RankingProviderState ProviderState,
    RankingLogRow? Ranking);

internal readonly record struct RankingDroppedCandidate(
    string Path,
    RankingCandidateSource Source,
    string? ProviderId,
    RankingProviderState ProviderState,
    int KernelRank,
    RankingDropStage DropStage,
    RankingLogRow? Ranking = null);

internal sealed record RankingPageTrace(
    RankingLogSession Session,
    RankingPageCounts Counts,
    IReadOnlyList<RankingDisplayedRow> DisplayedRows,
    IReadOnlyList<RankingDroppedCandidate> DroppedCandidates,
    IReadOnlyList<RankingProviderTrace> Providers,
    IReadOnlyList<string>? TypeOrder,
    bool IsFinal);

internal readonly record struct RankingPageStamp(
    string SearchSessionId,
    string PageId,
    RankingTraceWindow Window,
    long QuerySequence);

/// <summary>Structured input to the user-triggered, localization-independent explanation formatter.</summary>
internal sealed record RankingExplanation(
    RankingLogSession Session,
    RankingPageStamp Page,
    RankingPageCounts Counts,
    RankingDisplayedRow Row);
