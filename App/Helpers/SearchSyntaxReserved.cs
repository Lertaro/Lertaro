using Lertaro.App.Services;

namespace Lertaro.App.Helpers;

// Fixed syntax and provider characters stay reserved; trigger fields also reserve the current draft prefix.
public static class SearchSyntaxReserved
{
    public const char TokenPrefixCharacter = '\\';

    public const char PrecisionInversionCharacter = '?';

    public static IReadOnlyList<char> LeadingCharacters => GetLeadingCharacters(GlobalTokenPrefix.Current);
    private static char[] GetLeadingCharacters(char prefix) => [prefix, '<', '>', ':', '*', '/', PrecisionInversionCharacter, '|'];

    public static IReadOnlyList<char> ProviderClaimedCharacters { get; } = new[] { '#', '$', '%' };

    public static bool IsReserved(char value) => LeadingCharacters.Contains(value);

    // Whether a word opening with this character is read as syntax rather than as text. One answer for
    // both directions: a trigger field may not claim such a character, and a query that opens with one
    // is not a file name being typed.
    public static bool OpensSyntax(char value, char? prefix = null)
        => GetLeadingCharacters(prefix ?? GlobalTokenPrefix.Current).Contains(value)
            || ProviderClaimedCharacters.Contains(value);

    public static IReadOnlyList<char> UnusableTokenPrefixCharacters { get; } =
        new[] { '<', '>', ':', '*', '/', PrecisionInversionCharacter, '|', '#', '$', '%', '"' };

    public static string DescribeUnusableTokenPrefixCharacters() => string.Join(' ', UnusableTokenPrefixCharacters);

    // The characters a working Windows file name can never contain.
    public static IReadOnlyList<char> FileNameIllegalCharacters { get; } =
        new[] { '\\', '/', ':', '*', PrecisionInversionCharacter, '"', '<', '>', '|' };

    /// <summary>
    /// Whether this character may start a plugin query token.
    /// </summary>
    /// <remarks>
    /// It has to be a character a file name cannot contain, because the scanner lifts a WHOLE WORD that opens
    /// with the prefix: a prefix a name can contain turns a real search into a token with no provider to
    /// claim it, and that answers with an empty result set. The syntax already owns every illegal character
    /// except '\', so that is the only usable prefix -- which is why the field reports every other value
    /// instead of warning about the risky ones (decision 2026-10-28; the warning it replaces said the words
    /// "may" be read as tokens, which was true of every legal character and therefore of no help).
    /// </remarks>
    public static bool IsUsableTokenPrefix(char value)
        => FileNameIllegalCharacters.Contains(value) && !UnusableTokenPrefixCharacters.Contains(value);

    public static string? ValidateLeadingCharacter(string? value, char? prefix = null)
    {
        if (string.IsNullOrEmpty(value))
            return TranslationManager.Instance["General_ReservedCharacterEmpty"];

        var first = value[0];
        if (OpensSyntax(first, prefix))
        {
            var reserved = GetLeadingCharacters(prefix ?? GlobalTokenPrefix.Current);
            return reserved.Contains(first)
                ? string.Format(
                    TranslationManager.Instance["General_ReservedCharacterSyntax"], string.Join(' ', reserved))
                : TranslationManager.Instance["General_ReservedCharacterProvider"];
        }

        return null;
    }

    public static string DescribeLeadingCharacters() => string.Join(' ', LeadingCharacters);
}
