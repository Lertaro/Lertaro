using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lertaro.Core.SearchIndex;

/// <summary>Formats one row as invariant key/value text for the explicit Copy action.</summary>
internal static class RankingExplanationFormatter
{
    internal static string Format(
        RankingExplanation explanation,
        string rawQuery,
        string displayName,
        string path)
    {
        var row = explanation.Row;
        var ranked = row.Ranking;
        var lines = new List<string>
        {
            // This text is not hashed: it carries the query, the display name and the path in the clear.
            // Said out loud on the first line because the paste is what reaches a bug report.
            "# plaintext: the query, display name and path below are written as typed",
            $"rankingTraceVersion={RankingTrace.SchemaVersion.ToString(CultureInfo.InvariantCulture)}",
            $"rankingPolicyVersion={RankPolicy.Version.ToString(CultureInfo.InvariantCulture)}",
            $"searchSessionId={explanation.Session.SearchSessionId}",
            $"pageId={explanation.Page.PageId}",
            $"window={WindowId(explanation.Page.Window)}",
            $"querySequence={explanation.Page.QuerySequence.ToString(CultureInfo.InvariantCulture)}",
            $"query={JsonSerializer.Serialize(rawQuery)}",
            $"query.rawHash={explanation.Session.RawQueryHash}",
            $"query.canonicalHash={explanation.Session.CanonicalQueryHash}",
            $"query.variantFlags={explanation.Session.QueryVariantFlags.ToString(CultureInfo.InvariantCulture)}",
            $"result.name={JsonSerializer.Serialize(displayName)}",
            $"result.path={JsonSerializer.Serialize(path)}",
            $"result.pathHash={RankingTrace.HashKey(path)}",
            $"result.finalDisplayedRank={row.FinalDisplayedRank.ToString(CultureInfo.InvariantCulture)}",
            $"result.source={SourceId(row.Source)}",
            $"result.selectionStage={SelectionId(row.SelectionStage)}",
            $"result.providerId={row.ProviderId ?? "unknown"}",
            $"result.providerState={ProviderStateId(row.ProviderState)}",
            $"counts.engineCandidates={explanation.Counts.EngineCandidateCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.afterMapperFilter={explanation.Counts.AfterMapperFilterCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.preMerge={explanation.Counts.PreMergeCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.postMerge={explanation.Counts.PostMergeCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.prefix={explanation.Counts.PrefixCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.typeCoverageSelected={explanation.Counts.TypeCoverageSelectedCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.beforeFinalCap={explanation.Counts.BeforeFinalCapCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.tokenInput={explanation.Counts.TokenInputCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.tokenOutput={explanation.Counts.TokenOutputCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.finalCapDropped={explanation.Counts.FinalCapDroppedCount.ToString(CultureInfo.InvariantCulture)}",
            $"counts.finalShown={explanation.Counts.FinalShownCount.ToString(CultureInfo.InvariantCulture)}",
        };

        if (ranked is { } rank)
        {
            lines.Add($"rank.kernel={rank.KernelRank.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.section={((int)rank.Section).ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.score={rank.Score.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.type={rank.TypeRank.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.priority={((int)rank.Priority).ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.historyTier={((int)rank.Tier).ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.matchTier={rank.Match.Tier.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.matchShape={rank.Match.Shape.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.matchStart={rank.Match.Start.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"rank.matchCoverage={rank.Match.Coverage.ToString("0.###", CultureInfo.InvariantCulture)}");
            lines.Add($"rank.matchContiguity={rank.Match.Contiguity.ToString("0.###", CultureInfo.InvariantCulture)}");
            lines.Add($"evidence.field={rank.Evidence.Field}");
            lines.Add($"evidence.mode={rank.Evidence.Mode}");
            lines.Add($"evidence.queryVariant={rank.Evidence.QueryVariant}");
            lines.Add($"evidence.grade={rank.Evidence.Grade}");
            lines.Add($"evidence.allPositiveTerms={(rank.Evidence.AllPositiveTerms ? "true" : "false")}");
            lines.Add($"evidence.directText={(rank.Evidence.DirectText ? "true" : "false")}");
            lines.Add($"evidence.providerId={rank.Evidence.ProviderId ?? "unknown"}");
            lines.Add($"evidence.spanStart={(rank.Evidence.SpanStart >= 0 ? rank.Evidence.SpanStart.ToString(CultureInfo.InvariantCulture) : "unknown")}");
            lines.Add($"evidence.spanLength={rank.Evidence.SpanLength.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"candidate.kindFlags={rank.Identity.Kind}");
        }
        else
        {
            lines.Add("rank.kernel=unknown");
        }

        return string.Join('\n', lines);
    }

    private static string WindowId(RankingTraceWindow value) => value switch
    {
        RankingTraceWindow.Quick => "quick",
        RankingTraceWindow.Inline => "inline",
        RankingTraceWindow.Full => "full",
        _ => "unknown",
    };

    private static string ProviderStateId(RankingProviderState value) => value switch
    {
        RankingProviderState.NotStarted => "not-started",
        RankingProviderState.Loading => "loading",
        RankingProviderState.ReadyEmpty => "ready-empty",
        RankingProviderState.ReadyWithItems => "ready-with-items",
        RankingProviderState.Stale => "stale",
        _ => "unknown",
    };

    private static string SelectionId(RankingSelectionStage value) => value switch
    {
        RankingSelectionStage.PluginPrefix => "plugin-prefix",
        RankingSelectionStage.KernelOrder => "kernel-order",
        RankingSelectionStage.TypeCoverage => "type-coverage",
        RankingSelectionStage.TokenOrder => "token-order",
        _ => "unknown",
    };

    private static string SourceId(RankingCandidateSource value)
    {
        if (value == RankingCandidateSource.Unknown)
            return "unknown";

        var builder = new StringBuilder();
        Append(RankingCandidateSource.FileIndex, "file-index");
        Append(RankingCandidateSource.SearchableProvider, "searchable-provider");
        Append(RankingCandidateSource.Favorite, "favorite");
        Append(RankingCandidateSource.History, "history");
        Append(RankingCandidateSource.WindowsRecent, "windows-recent");
        Append(RankingCandidateSource.InstantProvider, "instant-provider");
        Append(RankingCandidateSource.PluginAction, "plugin-action");
        Append(RankingCandidateSource.Synthetic, "synthetic");
        return builder.Length == 0 ? "unknown" : builder.ToString();

        void Append(RankingCandidateSource flag, string id)
        {
            if ((value & flag) == 0)
                return;
            if (builder.Length > 0)
                builder.Append('|');
            builder.Append(id);
        }
    }
}
