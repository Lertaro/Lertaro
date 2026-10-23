using System.Text.Json;

namespace Lertaro.Core.SearchIndex;

/// <summary>Normalized view used by diagnostics readers across schema versions.</summary>
internal readonly record struct RankingEventRecord(
    int SchemaVersion,
    string EventKind,
    string? SearchSessionId,
    string? PageId,
    int? FinalDisplayedRank,
    int? KernelRank,
    RankingCandidateSource Source,
    RankingDropStage DropStage,
    RankingProviderState ProviderState,
    MatchEvidenceField EvidenceField,
    MatchEvidenceMode EvidenceMode,
    MatchQueryVariant QueryVariant,
    MatchEvidenceGrade EvidenceGrade,
    RankingCandidateKind Kind);

/// <summary>Reads JSONL across schema versions; absent or unknown values normalize to Unknown/null.</summary>
internal static class RankingEventLogReader
{
    internal static bool TryReadLine(string line, out RankingEventRecord record)
    {
        record = default;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var version = Int(root, "v") ?? 0;
            var kind = String(root, "kind") ?? "unknown";
            var source = SourceValue(Int(root, "source"));
            var drop = EnumValue(Int(root, "drop"), RankingDropStage.Unknown);
            var providerState = EnumValue(Int(root, "providerState"), RankingProviderState.Unknown);
            var finalRank = version >= 3 ? Int(root, "finalDisplayedRank") ?? Int(root, "rank") : null;
            var kernelRank = version >= 3 ? Int(root, "kernelRank") : Int(root, "rank");
            record = new RankingEventRecord(
                version,
                kind,
                String(root, "searchSessionId"),
                String(root, "pageId"),
                finalRank,
                kernelRank,
                source,
                drop,
                providerState,
                version >= 3 ? EnumValue(Int(root, "field"), MatchEvidenceField.LegacyUnknown) : MatchEvidenceField.LegacyUnknown,
                version >= 3 ? EnumValue(Int(root, "mode"), MatchEvidenceMode.LegacyUnknown) : MatchEvidenceMode.LegacyUnknown,
                version >= 3 ? EnumValue(Int(root, "queryVariant"), MatchQueryVariant.LegacyUnknown) : MatchQueryVariant.LegacyUnknown,
                version >= 3 ? EnumValue(Int(root, "evidenceGrade"), MatchEvidenceGrade.LegacyUnknown) : MatchEvidenceGrade.LegacyUnknown,
                version >= 3 ? KindValue(Int(root, "kindFlags")) : RankingCandidateKind.Unknown);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static RankingCandidateSource SourceValue(int? raw)
    {
        const RankingCandidateSource all =
            RankingCandidateSource.FileIndex |
            RankingCandidateSource.SearchableProvider |
            RankingCandidateSource.Favorite |
            RankingCandidateSource.History |
            RankingCandidateSource.WindowsRecent |
            RankingCandidateSource.InstantProvider |
            RankingCandidateSource.PluginAction |
            RankingCandidateSource.Synthetic;
        return raw is { } value && value >= 0 &&
               ((RankingCandidateSource)value & ~all) == RankingCandidateSource.Unknown
            ? (RankingCandidateSource)value
            : RankingCandidateSource.Unknown;
    }

    private static RankingCandidateKind KindValue(int? raw)
    {
        const RankingCandidateKind all =
            RankingCandidateKind.File |
            RankingCandidateKind.Directory |
            RankingCandidateKind.Application |
            RankingCandidateKind.Shortcut;
        return raw is { } value && value >= 0 &&
               ((RankingCandidateKind)value & ~all) == RankingCandidateKind.Unknown
            ? (RankingCandidateKind)value
            : RankingCandidateKind.Unknown;
    }

    private static T EnumValue<T>(int? raw, T fallback) where T : struct, Enum
    {
        if (raw is not { } value)
            return fallback;
        foreach (var known in Enum.GetValues<T>())
        {
            if (Convert.ToInt32(known) == value)
                return known;
        }
        return fallback;
    }
}
