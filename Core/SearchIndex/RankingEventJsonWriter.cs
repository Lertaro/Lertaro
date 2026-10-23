using System.Text;

namespace Lertaro.Core.SearchIndex;

internal static class RankingEventJsonWriter
{
    internal static string PageJson(long timestamp, RankingPageStamp stamp, RankingPageTrace trace)
    {
        var c = trace.Counts;
        return new StringBuilder()
            .Append("{\"ts\":").Append(timestamp)
            .Append(",\"v\":").Append(RankingEventLog.SchemaVersion)
            .Append(",\"kind\":\"page\"")
            .Append(",\"searchSessionId\":\"").Append(stamp.SearchSessionId).Append('"')
            .Append(",\"pageId\":\"").Append(stamp.PageId).Append('"')
            .Append(",\"window\":").Append((int)stamp.Window)
            .Append(",\"querySeq\":").Append(stamp.QuerySequence)
            .Append(",\"queryHash\":\"").Append(trace.Session.CanonicalQueryHash).Append('"')
            .Append(",\"rawQueryHash\":\"").Append(trace.Session.RawQueryHash).Append('"')
            .Append(",\"canonicalQueryHash\":\"").Append(trace.Session.CanonicalQueryHash).Append('"')
            .Append(",\"queryVariantFlags\":").Append(trace.Session.QueryVariantFlags)
            .Append(",\"engineCandidates\":").Append(c.EngineCandidateCount)
            .Append(",\"afterMapperFilter\":").Append(c.AfterMapperFilterCount)
            .Append(",\"preMergeCount\":").Append(c.PreMergeCount)
            .Append(",\"postMergeCount\":").Append(c.PostMergeCount)
            .Append(",\"prefixCount\":").Append(c.PrefixCount)
            .Append(",\"typeCoverageSelected\":").Append(c.TypeCoverageSelectedCount)
            .Append(",\"beforeFinalCapCount\":").Append(c.BeforeFinalCapCount)
            .Append(",\"finalShownCount\":").Append(c.FinalShownCount)
            .Append(",\"tokenInputCount\":").Append(c.TokenInputCount)
            .Append(",\"tokenOutputCount\":").Append(c.TokenOutputCount)
            .Append(",\"finalCapDropped\":").Append(c.FinalCapDroppedCount)
            .Append(",\"rows\":").Append(c.PostMergeCount)
            .Append(",\"shown\":").Append(trace.DisplayedRows.Count)
            .Append(",\"isFinal\":").Append(Flag(trace.IsFinal))
            .Append(",\"order\":[").Append(OrderJson(trace.TypeOrder)).Append(']')
            .Append(",\"providers\":").Append(ProvidersJson(trace.Providers))
            .Append(",\"policyVersion\":").Append(RankPolicy.Version)
            .Append('}')
            .ToString();
    }

    internal static string DisplayedRowJson(
        long timestamp,
        RankingPageStamp stamp,
        RankingLogSession session,
        string pathHash,
        RankingDisplayedRow row)
    {
        var builder = EventPrefix(timestamp, "row", stamp, session, pathHash)
            .Append(",\"rank\":").Append(row.FinalDisplayedRank)
            .Append(",\"finalDisplayedRank\":").Append(row.FinalDisplayedRank)
            .Append(",\"source\":").Append((int)row.Source)
            .Append(",\"selectionStage\":").Append((int)row.SelectionStage)
            .Append(",\"providerId\":\"").Append(Escape(row.ProviderId ?? string.Empty)).Append('"')
            .Append(",\"providerState\":").Append((int)row.ProviderState)
            .Append(",\"providerReady\":").Append(ProviderReady(row.ProviderState))
            .Append(",\"engineCandidate\":").Append(Flag((row.Source & RankingCandidateSource.FileIndex) != 0))
            .Append(",\"merged\":").Append(Flag(HasMultipleFlags(row.Source)))
            .Append(",\"displayed\":1,\"drop\":").Append((int)RankingDropStage.Displayed);
        if (row.Ranking is { } ranked)
            AppendRankFields(builder, ranked);
        else
            AppendUnknownRankFields(builder, row.Path);
        return builder.Append(",\"opened\":false}").ToString();
    }

    internal static string DroppedCandidateJson(
        long timestamp,
        RankingPageStamp stamp,
        RankingLogSession session,
        RankingDroppedCandidate candidate)
    {
        var builder = EventPrefix(timestamp, "candidate", stamp, session, RankingEventLog.HashKey(candidate.Path))
            .Append(",\"source\":").Append((int)candidate.Source)
            .Append(",\"providerId\":\"").Append(Escape(candidate.ProviderId ?? string.Empty)).Append('"')
            .Append(",\"providerState\":").Append((int)candidate.ProviderState)
            .Append(",\"providerReady\":").Append(ProviderReady(candidate.ProviderState))
            .Append(",\"engineCandidate\":").Append(Flag((candidate.Source & RankingCandidateSource.FileIndex) != 0))
            .Append(",\"merged\":").Append(Flag(HasMultipleFlags(candidate.Source)))
            .Append(",\"displayed\":0,\"drop\":").Append((int)candidate.DropStage);
        if (candidate.Ranking is { } ranked)
            AppendRankFields(builder, ranked);
        else
        {
            builder.Append(",\"kernelRank\":").Append(candidate.KernelRank > 0 ? candidate.KernelRank : -1);
            AppendUnknownRankFields(builder, candidate.Path);
        }
        return builder.Append(",\"opened\":false}").ToString();
    }

    private static StringBuilder EventPrefix(
        long timestamp,
        string kind,
        RankingPageStamp stamp,
        RankingLogSession session,
        string pathHash) =>
        new StringBuilder()
            .Append("{\"ts\":").Append(timestamp)
            .Append(",\"v\":").Append(RankingEventLog.SchemaVersion)
            .Append(",\"kind\":\"").Append(kind).Append('"')
            .Append(",\"searchSessionId\":\"").Append(stamp.SearchSessionId).Append('"')
            .Append(",\"pageId\":\"").Append(stamp.PageId).Append('"')
            .Append(",\"window\":").Append((int)stamp.Window)
            .Append(",\"querySeq\":").Append(stamp.QuerySequence)
            .Append(",\"queryHash\":\"").Append(session.CanonicalQueryHash).Append('"')
            .Append(",\"rawQueryHash\":\"").Append(session.RawQueryHash).Append('"')
            .Append(",\"canonicalQueryHash\":\"").Append(session.CanonicalQueryHash).Append('"')
            .Append(",\"pathHash\":\"").Append(pathHash).Append('"');

    private static void AppendRankFields(StringBuilder builder, RankingLogRow row)
    {
        builder.Append(",\"kernelRank\":").Append(row.KernelRank)
            .Append(",\"bucket\":").Append((int)row.Section)
            .Append(",\"score\":").Append(row.Score)
            .Append(",\"typeRank\":").Append(row.TypeRank)
            .Append(",\"prio\":").Append(PriorityWeight(row.Priority))
            .Append(",\"hTier\":").Append((int)row.Tier)
            .Append(",\"histTie\":").Append(row.HistoryTie)
            .Append(",\"fav\":").Append(Flag(row.IsFavorite))
            .Append(",\"histOnly\":").Append(Flag(row.IsHistoryOnly))
            .Append(",\"strong\":").Append(Flag(row.Match.IsStrong))
            .Append(",\"mTier\":").Append(row.Match.Tier)
            .Append(",\"mShape\":").Append(row.Match.Shape)
            .Append(",\"mStart\":").Append(row.Match.Start)
            .Append(",\"mCov\":").Append(Permille(row.Match.Coverage))
            .Append(",\"mCont\":").Append(Permille(row.Match.Contiguity))
            .Append(",\"dir\":").Append(Flag(row.IsDir))
            .Append(",\"app\":").Append(Flag(row.IsApplication))
            .Append(",\"nameLen\":").Append(NameLength(row.Path))
            .Append(",\"depth\":").Append(Depth(row.Path))
            .Append(",\"ext\":").Append(ExtensionClass(row.Path))
            .Append(",\"field\":").Append((int)row.Evidence.Field)
            .Append(",\"mode\":").Append((int)row.Evidence.Mode)
            .Append(",\"queryVariant\":").Append((int)row.Evidence.QueryVariant)
            .Append(",\"evidenceGrade\":").Append((int)row.Evidence.Grade)
            .Append(",\"allPositiveTerms\":").Append(Flag(row.Evidence.AllPositiveTerms))
            .Append(",\"directText\":").Append(Flag(row.Evidence.DirectText))
            .Append(",\"evidenceProviderId\":\"").Append(Escape(row.Evidence.ProviderId ?? string.Empty)).Append('"')
            .Append(",\"spanStart\":").Append(row.Evidence.SpanStart)
            .Append(",\"spanLength\":").Append(row.Evidence.SpanLength)
            .Append(",\"kindFlags\":").Append((int)(row.Identity.Kind != RankingCandidateKind.Unknown
                ? row.Identity.Kind
                : CandidateIdentity.Create(row.Source, row.IsDir, row.IsApplication, string.Empty, row.Path).Kind))
            .Append(",\"policyVersion\":").Append(RankPolicy.Version);
        if (row.ShadowV3Rank is { } shadowRank)
            builder.Append(",\"shadowRank\":").Append(shadowRank);
        if (row.ShadowV3RankDelta is { } shadowDelta)
            builder.Append(",\"shadowRankDelta\":").Append(shadowDelta);
    }

    private static void AppendUnknownRankFields(StringBuilder builder, string path) =>
        builder.Append(",\"bucket\":-1,\"score\":-1,\"typeRank\":-1,\"prio\":-1")
            .Append(",\"hTier\":0,\"histTie\":0,\"strong\":0")
            .Append(",\"mTier\":-1,\"mShape\":-1,\"mStart\":-1,\"mCov\":0,\"mCont\":0")
            .Append(",\"nameLen\":").Append(NameLength(path))
            .Append(",\"depth\":").Append(Depth(path))
            .Append(",\"ext\":").Append(ExtensionClass(path))
            .Append(",\"field\":0,\"mode\":0,\"queryVariant\":0,\"evidenceGrade\":0")
            .Append(",\"allPositiveTerms\":0,\"directText\":0,\"evidenceProviderId\":\"\"")
            .Append(",\"spanStart\":-1,\"spanLength\":0,\"kindFlags\":0")
            .Append(",\"policyVersion\":").Append(RankPolicy.Version);

    private static string ProvidersJson(IReadOnlyList<RankingProviderTrace> providers)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < providers.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            var provider = providers[i];
            builder.Append("{\"id\":\"").Append(Escape(provider.ProviderId)).Append('"')
                .Append(",\"state\":").Append((int)provider.State)
                .Append(",\"ready\":").Append(ProviderReady(provider.State))
                .Append(",\"items\":").Append(provider.ItemCount)
                .Append(",\"hits\":").Append(provider.HitCount)
                .Append('}');
        }
        return builder.Append(']').ToString();
    }

    private static string OrderJson(IReadOnlyList<string>? typeOrder)
    {
        if (typeOrder is null || typeOrder.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        for (var i = 0; i < typeOrder.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append('"').Append(Escape(typeOrder[i])).Append('"');
        }
        return builder.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static int PriorityWeight(IndexRuleEffect effect) => effect switch
    {
        IndexRuleEffect.High => 2,
        IndexRuleEffect.Normal => 1,
        _ => 0,
    };

    private static int ProviderReady(RankingProviderState state) => state switch
    {
        RankingProviderState.ReadyEmpty or RankingProviderState.ReadyWithItems or RankingProviderState.Stale => 1,
        RankingProviderState.NotStarted or RankingProviderState.Loading => 0,
        _ => -1,
    };

    private static bool HasMultipleFlags(RankingCandidateSource source)
    {
        var bits = (uint)source;
        return bits != 0 && (bits & (bits - 1)) != 0;
    }

    private static int Flag(bool value) => value ? 1 : 0;

    private static int Permille(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 1000, MidpointRounding.AwayFromZero);

    private static int NameLength(string path) => Path.GetFileName(path).Length;

    private static int Depth(string path) => path.Count(c => c is '\\' or '/') + 1;

    private static int ExtensionClass(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return 0;
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
            return 4;
        return 5;
    }

}
