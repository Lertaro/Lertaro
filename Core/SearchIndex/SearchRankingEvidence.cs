namespace Lertaro.Core.SearchIndex;

public enum MatchEvidenceField : byte
{
    LegacyUnknown = 0,
    DisplayTitle = 1,
    FileName = 2,
    CuratedAlias = 3,
    ShortcutTarget = 5,
    Keyword = 6,
    ParentPath = 7,
    HistoryKeyword = 8,
    ProviderAlias = 9,
    ProductMetadata = 10,
}

public enum MatchEvidenceMode : byte
{
    LegacyUnknown = 0,
    Literal = 1,
    PinyinInitials = 2,
    PinyinFull = 3,
    PinyinRare = 4,
    Fuzzy = 5,
    LearnedAlias = 6,
}

public enum MatchQueryVariant : byte
{
    LegacyUnknown = 0,
    Original = 1,
    PinyinJoined = 2,
    ProviderAlias = 3,
    HistoryExact = 4,
}

/// <summary>Smaller values are stronger; zero is unknown and never participates in ordering.</summary>
public enum MatchEvidenceGrade : byte
{
    LegacyUnknown = 0,
    ExactIdentity = 1,
    DirectStrong = 2,
    DirectApproximate = 3,
    SecondaryField = 4,
    LearnedAlias = 5,
}

[Flags]
public enum RankingCandidateKind : byte
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
public readonly record struct MatchEvidence(
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
        int spanLength = 0,
        bool isDirectory = false)
    {
        if (!rank.IsMatch)
            return LegacyUnknown;

        var direct = field is MatchEvidenceField.DisplayTitle or MatchEvidenceField.FileName;
        var grade = direct
            ? IsExactIdentityOrWholeStemReading(
                query, matchedText, field, rank, variant, spanStart, spanLength, isDirectory)
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

    /// <summary>
    /// Resolves a deterministic V3 grade purely from precomputed candidate fields without re-matching text.
    /// </summary>
    public static MatchEvidenceGrade ResolveGrade(
        in MatchEvidence evidence,
        in MatchRank match,
        bool isHistoryOnly,
        bool isStrongMatch)
    {
        if (isHistoryOnly || evidence.Grade == MatchEvidenceGrade.LearnedAlias)
            return MatchEvidenceGrade.LearnedAlias;

        if (evidence.Grade != MatchEvidenceGrade.LegacyUnknown)
            return evidence.Grade;

        if (match.Shape == MatchRank.ShapeNoName)
            return MatchEvidenceGrade.SecondaryField;

        return isStrongMatch
            ? MatchEvidenceGrade.DirectStrong
            : MatchEvidenceGrade.DirectApproximate;
    }

    internal static bool IsExactIdentityOrWholeStemReading(
        string query,
        string text,
        MatchEvidenceField field,
        in MatchRank rank,
        MatchQueryVariant variant,
        int spanStart = -1,
        int spanLength = 0,
        bool isDirectory = false)
    {
        var trimExtension = field == MatchEvidenceField.FileName && !isDirectory;
        if (IsExactIdentity(query, text, trimExtension))
            return true;

        // A common reading walk (initials or full pinyin, never a rare polyphone) covering the whole
        // stem from index 0 represents the user's complete romanized spelling ("pixpin", "cfwj",
        // "sousuojieguo", "sousuo jieguo"). Multi-token queries only earn ExactIdentity when matched via
        // the consecutive joined-reading branch across [0..stem.Length); independent tokens on swapped
        // syllables ("sousuo jieguo" against "结果搜索.txt") union into full marks without sequential order.
        if (rank.Tier is not (MatchRank.TierInitials or MatchRank.TierFull))
            return false;
        if (rank.Start != 0 || rank.Contiguity < 1d)
            return false;

        var fullSpan = text.AsSpan().Trim();
        var stemSpan = trimExtension ? StripExtensionIfNonEmpty(fullSpan) : fullSpan;
        if (fullSpan.Length == 0 || stemSpan.Length == 0)
            return false;

        var coveredChars = (int)Math.Round(rank.Coverage * fullSpan.Length, MidpointRounding.AwayFromZero);
        if (coveredChars != stemSpan.Length)
            return false;

        if (!ContainsWhitespace(query.AsSpan().Trim()))
            return true;

        return variant == MatchQueryVariant.PinyinJoined
               && spanStart == 0
               && spanLength == stemSpan.Length;
    }

    private static ReadOnlySpan<char> StripExtensionIfNonEmpty(ReadOnlySpan<char> fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        return stem.Length > 0 ? stem : fileName;
    }

    private static bool ContainsWhitespace(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                return true;
        }
        return false;
    }

    private static bool IsExactIdentity(string query, string text, bool trimExtension)
    {
        var querySpan = query.AsSpan().Trim();
        var textSpan = trimExtension
            ? StripExtensionIfNonEmpty(text.AsSpan().Trim())
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
        var isShortcut = !isDirectory && IsShortcutExtension(Path.GetExtension(path));
        if (isShortcut)
            kind |= RankingCandidateKind.Shortcut;
        return new CandidateIdentity(
            source,
            kind,
            ResolveSearchTitle(title, path, isShortcut),
            path.TrimEnd('\\'));
    }

    private static string ResolveSearchTitle(string title, string path, bool isShortcut)
    {
        var candidate = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileName(path.TrimEnd('\\', '/'))
            : title.Trim();
        if (!isShortcut || !IsShortcutExtension(Path.GetExtension(candidate)))
            return candidate;

        // Shortcut containers are identity-transparent; executable and ordinary file extensions are not.
        // In particular, PixPin.exe must remain a file title rather than being upgraded to the PixPin app.
        var withoutExtension = Path.GetFileNameWithoutExtension(candidate);
        return string.IsNullOrWhiteSpace(withoutExtension) ? candidate : withoutExtension;
    }

    private static bool IsShortcutExtension(string extension) =>
        extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase);
}
