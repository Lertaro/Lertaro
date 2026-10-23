namespace Lertaro.Core.SearchIndex;

internal enum MatchEvidenceField : byte
{
    LegacyUnknown = 0,
    DisplayTitle = 1,
    FileName = 2,
    CuratedAlias = 3,
    UserAlias = 4,
    ShortcutTarget = 5,
    Keyword = 6,
    ParentPath = 7,
    HistoryKeyword = 8,
    ProviderAlias = 9,
}

internal enum MatchEvidenceMode : byte
{
    LegacyUnknown = 0,
    Literal = 1,
    PinyinInitials = 2,
    PinyinFull = 3,
    PinyinRare = 4,
    Fuzzy = 5,
    LearnedAlias = 6,
}

internal enum MatchQueryVariant : byte
{
    LegacyUnknown = 0,
    Original = 1,
    PinyinJoined = 2,
    ProviderAlias = 3,
    HistoryExact = 4,
}

/// <summary>Smaller values are stronger; zero is unknown and never participates in ordering.</summary>
internal enum MatchEvidenceGrade : byte
{
    LegacyUnknown = 0,
    ExactIdentity = 1,
    DirectStrong = 2,
    DirectApproximate = 3,
    SecondaryField = 4,
    LearnedAlias = 5,
}

[Flags]
internal enum RankingCandidateKind : byte
{
    Unknown = 0,
    // Directory/Application keep the schema-3 bit positions written before File/Shortcut were explicit.
    Directory = 1 << 0,
    Application = 1 << 1,
    Shortcut = 1 << 2,
    File = 1 << 3,
}

/// <summary>
/// Shadow evidence captured from the representation that actually matched. Mode and span stay
/// LegacyUnknown until the matcher returns its accepted branch; they are never inferred from MatchRank.
/// </summary>
internal readonly record struct MatchEvidence(
    MatchEvidenceField Field,
    MatchEvidenceMode Mode,
    MatchQueryVariant QueryVariant,
    MatchEvidenceGrade Grade,
    bool AllPositiveTerms,
    bool DirectText,
    string? ProviderId = null,
    int SpanStart = -1,
    int SpanLength = 0)
{
    public static MatchEvidence LegacyUnknown => default;

    internal static MatchEvidence Accepted(
        string query,
        string matchedText,
        MatchEvidenceField field,
        in MatchRank rank,
        MatchEvidenceMode mode = MatchEvidenceMode.LegacyUnknown,
        MatchQueryVariant variant = MatchQueryVariant.Original,
        string? providerId = null,
        int spanStart = -1,
        int spanLength = 0)
    {
        if (!rank.IsMatch)
            return LegacyUnknown;

        var direct = field is MatchEvidenceField.DisplayTitle or MatchEvidenceField.FileName;
        var grade = direct
            ? IsExactIdentity(query, matchedText, field)
                ? MatchEvidenceGrade.ExactIdentity
                : rank.IsStrong
                    ? MatchEvidenceGrade.DirectStrong
                    : MatchEvidenceGrade.DirectApproximate
            : field == MatchEvidenceField.HistoryKeyword
                ? MatchEvidenceGrade.LearnedAlias
                : MatchEvidenceGrade.SecondaryField;
        return new MatchEvidence(
            field,
            mode,
            variant,
            grade,
            AllPositiveTerms: true,
            DirectText: direct,
            providerId,
            spanStart,
            spanLength);
    }

    internal static MatchEvidence Stronger(MatchEvidence preferred, MatchEvidence other)
    {
        if (preferred.Grade == MatchEvidenceGrade.LegacyUnknown)
            return other;
        if (other.Grade == MatchEvidenceGrade.LegacyUnknown)
            return preferred;
        return other.Grade < preferred.Grade ? other : preferred;
    }

    private static bool IsExactIdentity(string query, string text, MatchEvidenceField field)
    {
        var querySpan = query.AsSpan().Trim();
        var textSpan = field == MatchEvidenceField.FileName
            ? Path.GetFileNameWithoutExtension(text.AsSpan()).Trim()
            : text.AsSpan().Trim();
        if (querySpan.Length == 0)
            return false;
        if (!SearchTextNormalizer.NeedsNormalization(querySpan)
            && !SearchTextNormalizer.NeedsNormalization(textSpan))
        {
            return querySpan.Equals(textSpan, StringComparison.OrdinalIgnoreCase);
        }

        return SearchTextNormalizer.Normalize(querySpan.ToString()).Trim().Equals(
            SearchTextNormalizer.Normalize(textSpan.ToString()).Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct MatchEvidenceResult(MatchRank Rank, MatchEvidence Evidence);

internal readonly record struct AcceptedMatchWitness(
    MatchEvidenceMode Mode,
    MatchQueryVariant QueryVariant,
    string? ProviderId,
    int SpanStart,
    int SpanLength)
{
    public static AcceptedMatchWitness LegacyUnknown => new(
        MatchEvidenceMode.LegacyUnknown,
        MatchQueryVariant.LegacyUnknown,
        null,
        -1,
        0);
}

/// <summary>Source, execution kind and merge key of one candidate; ordering does not read this structure.</summary>
internal readonly record struct CandidateIdentity(
    RankingCandidateSource Source,
    RankingCandidateKind Kind,
    string SearchTitle,
    string PathKey)
{
    internal static CandidateIdentity Create(
        RankingCandidateSource source,
        bool isDirectory,
        bool isApplication,
        string title,
        string path)
    {
        var kind = isDirectory ? RankingCandidateKind.Directory : RankingCandidateKind.File;
        if (isApplication)
            kind |= RankingCandidateKind.Application;
        var extension = Path.GetExtension(path);
        if (!isDirectory && (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase)))
        {
            kind |= RankingCandidateKind.Shortcut;
        }
        return new CandidateIdentity(source, kind, title, path.TrimEnd('\\'));
    }
}
