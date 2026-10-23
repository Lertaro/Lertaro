using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search.Mapping;

/// <summary>Joins mapper diagnostics to the final quick-search page without affecting composition.</summary>
internal sealed class QuickSearchRankingTrace
{
    private readonly RankingLogSession _session;
    private readonly ConditionalWeakTable<List<AppSearchResult>, MapperTraceSnapshot> _passes = new();

    internal QuickSearchRankingTrace(string rawQuery, string canonicalQuery)
    {
        _session = RankingTrace.BeginSession(rawQuery, canonicalQuery, RankingTraceWindow.Quick);
    }

    internal RankingLogSession Session => _session;

    internal void RegisterMapperOutput(List<AppSearchResult> output, MapperTraceSnapshot snapshot)
    {
        try
        {
            lock (_passes)
            {
                _passes.Remove(output);
                _passes.Add(output, snapshot);
            }
        }
        catch
        {
            // Observability is optional; mapper output remains valid without an attached snapshot.
        }
    }

    /// <summary>Records the page sent to ReplaceResults, including token input/output boundaries.</summary>
    internal void RecordDisplayedPage(
        List<AppSearchResult> mapperOutput,
        IReadOnlyList<AppSearchResult> displayed,
        bool isFinal,
        IReadOnlyList<AppSearchResult>? tokenInputRows = null,
        IReadOnlyList<AppSearchResult>? tokenOutputRows = null)
    {
        try
        {
            RecordDisplayedPageCore(mapperOutput, displayed, isFinal, tokenInputRows, tokenOutputRows);
        }
        catch
        {
            // Diagnostics must not prevent ReplaceResults.
        }
    }

    private void RecordDisplayedPageCore(
        List<AppSearchResult> mapperOutput,
        IReadOnlyList<AppSearchResult> displayed,
        bool isFinal,
        IReadOnlyList<AppSearchResult>? tokenInputRows,
        IReadOnlyList<AppSearchResult>? tokenOutputRows)
    {
        // A failed trace must not leave a reused row joined to its previous page.
        foreach (var result in displayed)
        {
            if (!IsTraceableResult(result))
                continue;
            result.RankingExplanation = null;
        }

        MapperTraceSnapshot? snapshot;
        lock (_passes)
        {
            _passes.TryGetValue(mapperOutput, out snapshot);
            _passes.Remove(mapperOutput);
        }

        snapshot ??= MapperTraceSnapshot.Empty;
        var tokenMode = tokenInputRows != null;
        var providerById = snapshot.Providers
            .GroupBy(provider => provider.ProviderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var displayedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayEvents = new List<RankingDisplayedRow>();
        var displayObjects = new List<AppSearchResult>();
        var finalRank = 0;

        foreach (var result in displayed)
        {
            if (!IsTraceableResult(result))
                continue;

            finalRank++;
            var providerId = result.SourceProvider?.GetType().Name;
            var providerState = ProviderState(providerId, providerById, result);
            RankingLogRow? ranked = null;
            var source = ClassifySource(result);
            var selection = RankingSelectionStage.PluginPrefix;
            var kernelIndex = snapshot.Pass.IndexOf(result);
            if (kernelIndex >= 0)
            {
                ranked = snapshot.Pass.DescribeAt(kernelIndex, providerState);
                source = ranked.Value.Source;
                providerId = ranked.Value.ProviderId;
                selection = tokenMode
                    ? RankingSelectionStage.TokenOrder
                    : snapshot.TypeCoveragePaths.Contains(ranked.Value.Path)
                        ? RankingSelectionStage.TypeCoverage
                        : RankingSelectionStage.KernelOrder;
            }
            displayedPaths.Add(ranked?.Path ?? result.FullPath);

            displayEvents.Add(new RankingDisplayedRow(
                result.FullPath,
                finalRank,
                source,
                selection,
                providerId,
                providerState,
                ranked));
            displayObjects.Add(result);
        }

        var dropped = isFinal
            ? BuildDropped(snapshot, mapperOutput, displayedPaths, tokenMode, tokenInputRows, tokenOutputRows, providerById)
            : new List<RankingDroppedCandidate>();
        var finalCapDrops = dropped.Count(d => d.DropStage == RankingDropStage.FinalDisplayCap);
        var counts = snapshot.Counts with
        {
            FinalShownCount = displayEvents.Count,
            TokenInputCount = tokenInputRows?.Count ?? 0,
            TokenOutputCount = tokenOutputRows?.Count ?? 0,
            FinalCapDroppedCount = finalCapDrops,
        };
        var trace = new RankingPageTrace(
            _session,
            counts,
            displayEvents,
            dropped,
            snapshot.Providers,
            snapshot.TypeOrder,
            isFinal);
        var page = RankingTrace.ComposePage(trace);
        for (var i = 0; i < displayObjects.Count; i++)
        {
            var result = displayObjects[i];
            var row = displayEvents[i];
            result.RankingExplanation = new RankingExplanation(_session, page, counts, row);
        }
    }

    private static List<RankingDroppedCandidate> BuildDropped(
        MapperTraceSnapshot snapshot,
        IReadOnlyList<AppSearchResult> mapperOutput,
        IReadOnlySet<string> displayedPaths,
        bool tokenMode,
        IReadOnlyList<AppSearchResult>? tokenInputRows,
        IReadOnlyList<AppSearchResult>? tokenOutputRows,
        IReadOnlyDictionary<string, RankingProviderTrace> providerById)
    {
        var dropped = new List<RankingDroppedCandidate>(
            snapshot.PreRankingDrops.Count + snapshot.ShadowAliasCandidates.Count);
        foreach (var candidate in snapshot.PreRankingDrops)
        {
            var state = candidate.ProviderId != null && providerById.TryGetValue(candidate.ProviderId, out var provider)
                ? provider.State
                : candidate.ProviderState;
            dropped.Add(candidate with { ProviderState = state });
        }

        foreach (var candidate in snapshot.ShadowAliasCandidates)
        {
            var providerId = candidate.Result.SourceProvider?.GetType().Name;
            var providerState = ProviderState(providerId, providerById, candidate.Result);
            var ranked = snapshot.Pass.DescribeShadow(candidate, providerState);
            dropped.Add(new RankingDroppedCandidate(
                candidate.NormalizedPath,
                candidate.Source,
                providerId,
                providerState,
                -1,
                RankingDropStage.ShadowAliasGate,
                ranked));
        }

        string TracePath(AppSearchResult result)
        {
            var index = snapshot.Pass.IndexOf(result);
            return index >= 0 ? snapshot.Pass.Rows[index].NormalizedPath : result.FullPath;
        }

        var tokenInput = tokenMode
            ? new HashSet<string>(tokenInputRows!.Select(TracePath), StringComparer.OrdinalIgnoreCase)
            : null;
        var tokenOutput = tokenMode
            ? new HashSet<string>(tokenOutputRows!.Select(TracePath), StringComparer.OrdinalIgnoreCase)
            : null;

        // Token composition keeps instant answers but excludes other prefix domains.
        foreach (var result in mapperOutput)
        {
            if (!IsTraceableResult(result) || snapshot.Pass.IndexOf(result) >= 0 || displayedPaths.Contains(result.FullPath))
                continue;
            var providerId = result.SourceProvider?.GetType().Name;
            dropped.Add(new RankingDroppedCandidate(
                result.FullPath,
                ClassifySource(result),
                providerId,
                ProviderState(providerId, providerById, result),
                0,
                tokenMode ? RankingDropStage.TokenDomainFilter : RankingDropStage.FinalDisplayCap));
        }

        foreach (var merged in snapshot.Pass.MergedAway)
        {
            var providerId = merged.Result.SourceProvider?.GetType().Name;
            dropped.Add(new RankingDroppedCandidate(
                merged.NormalizedPath,
                merged.Source,
                providerId,
                ProviderState(providerId, providerById, merged.Result),
                0,
                RankingDropStage.MergeDedupe));
        }

        for (var i = 0; i < snapshot.Pass.Rows.Count; i++)
        {
            var candidate = snapshot.Pass.Rows[i];
            if (displayedPaths.Contains(candidate.NormalizedPath))
                continue;

            var stage = RankingDropStage.FinalDisplayCap;
            if (tokenMode)
            {
                if (!tokenInput!.Contains(candidate.NormalizedPath))
                    stage = RankingDropStage.TokenDomainFilter;
                else if (!tokenOutput!.Contains(candidate.NormalizedPath))
                    stage = RankingDropStage.TokenFilter;
            }

            var providerId = candidate.Result.SourceProvider?.GetType().Name;
            var providerState = ProviderState(providerId, providerById, candidate.Result);
            var ranked = snapshot.Pass.DescribeAt(i, providerState);
            dropped.Add(new RankingDroppedCandidate(
                candidate.NormalizedPath,
                candidate.Source,
                providerId,
                providerState,
                i + 1,
                stage,
                ranked));
        }
        return dropped;
    }

    private static bool IsTraceableResult(AppSearchResult result) =>
        !result.IsEmptyResult &&
        !result.IsSearchSectionHeader &&
        !string.IsNullOrWhiteSpace(result.FullPath) &&
        !string.Equals(result.FullPath, "__SHOW_MORE__", StringComparison.Ordinal);

    private static RankingCandidateSource ClassifySource(AppSearchResult result)
    {
        if (result.IsIndexBacked)
            return RankingCandidateSource.FileIndex;
        if (result.IsPluginSearchAction)
            return RankingCandidateSource.PluginAction;
        if (result.SourceProvider is ISearchableItemProvider)
            return RankingCandidateSource.SearchableProvider;
        if (result.SourceProvider is IInstantResultProvider || result.IsInstantResult)
            return RankingCandidateSource.InstantProvider;
        return RankingCandidateSource.Synthetic;
    }

    private static RankingProviderState ProviderState(
        string? providerId,
        IReadOnlyDictionary<string, RankingProviderTrace> providerById,
        AppSearchResult result)
    {
        if (providerId != null && providerById.TryGetValue(providerId, out var provider))
            return provider.State;
        // Instant rows exist only after their per-query provider has answered.
        return result.SourceProvider is IInstantResultProvider || result.IsInstantResult || result.IsPluginSearchAction
            ? RankingProviderState.ReadyWithItems
            : RankingProviderState.Unknown;
    }
}

internal sealed record MapperTraceSnapshot(
    RankingPassTrace Pass,
    RankingPageCounts Counts,
    IReadOnlyList<RankingDroppedCandidate> PreRankingDrops,
    IReadOnlyList<RankingProviderTrace> Providers,
    IReadOnlyList<string>? TypeOrder,
    IReadOnlySet<string> TypeCoveragePaths,
    IReadOnlyList<SearchResultMapper.RankedCandidate> ShadowAliasCandidates)
{
    internal MapperTraceSnapshot(
        RankingPassTrace pass,
        RankingPageCounts counts,
        IReadOnlyList<RankingDroppedCandidate> preRankingDrops,
        IReadOnlyList<RankingProviderTrace> providers,
        IReadOnlyList<string>? typeOrder,
        IReadOnlySet<string> typeCoveragePaths)
        : this(
            pass,
            counts,
            preRankingDrops,
            providers,
            typeOrder,
            typeCoveragePaths,
            Array.Empty<SearchResultMapper.RankedCandidate>())
    {
    }

    internal static readonly MapperTraceSnapshot Empty = new(
        RankingPassTrace.Empty,
        default,
        Array.Empty<RankingDroppedCandidate>(),
        Array.Empty<RankingProviderTrace>(),
        null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<SearchResultMapper.RankedCandidate>());
}
