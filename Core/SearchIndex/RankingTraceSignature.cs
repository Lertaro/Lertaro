using System.Text;

namespace Lertaro.Core.SearchIndex;

/// <summary>Fingerprints page diagnostics for adjacent repaint collapse.</summary>
internal static class RankingTraceSignature
{
    internal static string Compute(RankingPageTrace trace, IReadOnlyList<string> displayedPathHashes)
    {
        var builder = new StringBuilder(256 + (trace.DisplayedRows.Count + trace.DroppedCandidates.Count) * 48);
        Text(builder, trace.Session.SearchSessionId);
        Number(builder, trace.IsFinal ? 1 : 0);
        Counts(builder, trace.Counts);

        for (var i = 0; i < trace.DisplayedRows.Count; i++)
        {
            var row = trace.DisplayedRows[i];
            Text(builder, displayedPathHashes[i]);
            Number(builder, row.FinalDisplayedRank);
            Number(builder, (int)row.Source);
            Number(builder, (int)row.SelectionStage);
            Text(builder, row.ProviderId);
            Number(builder, (int)row.ProviderState);
            Ranking(builder, row.Ranking);
        }

        foreach (var candidate in trace.DroppedCandidates)
        {
            Text(builder, RankingEventLog.HashKey(candidate.Path));
            Number(builder, (int)candidate.Source);
            Text(builder, candidate.ProviderId);
            Number(builder, (int)candidate.ProviderState);
            Number(builder, candidate.KernelRank);
            Number(builder, (int)candidate.DropStage);
            Ranking(builder, candidate.Ranking);
        }

        foreach (var provider in trace.Providers)
        {
            Text(builder, provider.ProviderId);
            Number(builder, (int)provider.State);
            Number(builder, provider.ItemCount);
            Number(builder, provider.HitCount);
        }

        foreach (var type in trace.TypeOrder ?? Array.Empty<string>())
            Text(builder, type);

        return RankingEventLog.HashExact(builder.ToString());
    }

    private static void Counts(StringBuilder builder, RankingPageCounts counts)
    {
        Number(builder, counts.EngineCandidateCount);
        Number(builder, counts.AfterMapperFilterCount);
        Number(builder, counts.PreMergeCount);
        Number(builder, counts.PostMergeCount);
        Number(builder, counts.PrefixCount);
        Number(builder, counts.TypeCoverageSelectedCount);
        Number(builder, counts.BeforeFinalCapCount);
        Number(builder, counts.FinalShownCount);
        Number(builder, counts.TokenInputCount);
        Number(builder, counts.TokenOutputCount);
        Number(builder, counts.FinalCapDroppedCount);
    }

    private static void Ranking(StringBuilder builder, RankingLogRow? ranking)
    {
        if (ranking is not { } row)
        {
            Number(builder, -1);
            return;
        }

        Number(builder, row.KernelRank);
        Number(builder, (int)row.Section);
        Number(builder, row.Score);
        Number(builder, row.TypeRank);
        Number(builder, (int)row.Priority);
        Number(builder, (int)row.Tier);
        Number(builder, row.HistoryTie);
        Number(builder, row.Match.Tier);
        Number(builder, (int)row.Match.Shape);
        Number(builder, row.Match.Start);
        Number(builder, BitConverter.DoubleToInt64Bits(row.Match.Coverage));
        Number(builder, BitConverter.DoubleToInt64Bits(row.Match.Contiguity));
        Number(builder, row.IsFavorite ? 1 : 0);
        Number(builder, row.IsHistoryOnly ? 1 : 0);
        Number(builder, row.IsDir ? 1 : 0);
        Number(builder, row.IsApplication ? 1 : 0);
        Number(builder, (int)row.Source);
        Text(builder, row.ProviderId);
        Number(builder, (int)row.ProviderState);
        Number(builder, (int)row.Evidence.Field);
        Number(builder, (int)row.Evidence.Mode);
        Number(builder, (int)row.Evidence.QueryVariant);
        Number(builder, (int)row.Evidence.Grade);
        Number(builder, row.Evidence.AllPositiveTerms ? 1 : 0);
        Number(builder, row.Evidence.DirectText ? 1 : 0);
        Text(builder, row.Evidence.ProviderId);
        Number(builder, row.Evidence.SpanStart);
        Number(builder, row.Evidence.SpanLength);
        Number(builder, (int)(row.Identity.Kind != RankingCandidateKind.Unknown
            ? row.Identity.Kind
            : CandidateIdentity.Create(row.Source, row.IsDir, row.IsApplication, string.Empty, row.Path).Kind));
    }

    private static void Number(StringBuilder builder, long value) => builder.Append(value).Append(';');

    private static void Text(StringBuilder builder, string? value)
    {
        builder.Append(value?.Length ?? -1).Append(':').Append(value).Append(';');
    }
}
