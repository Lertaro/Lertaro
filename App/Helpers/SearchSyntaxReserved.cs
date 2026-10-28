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

    public static bool IsUnusableAsTokenPrefix(char value)
        => char.IsWhiteSpace(value) || char.IsControl(value) || UnusableTokenPrefixCharacters.Contains(value);

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
