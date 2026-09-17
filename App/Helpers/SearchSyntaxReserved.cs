using Lertaro.App.Services;

namespace Lertaro.App.Helpers;

// The characters the search box reads as SYNTAX rather than as text, and the rules that keep the various
// user-configurable trigger surfaces from claiming one of them.
//
// Several independent features are configured by "the user picks a leading character": a plugin query
// token prefix, an instant-answer trigger keyword, and the quick window's per-result-type trigger. All of
// them are matched against the START of the query, which is exactly where the search syntax does its own
// reading, so any of them can silently stop working by claiming a character the syntax consumes first.
// Nothing reports that today -- the keyword simply never matches, or the typed character is stripped and
// the feature looks broken with nothing on screen to explain it.
//
// This is the single place that says which characters those are. It applies the same rule to every
// surface so the answer cannot drift between them, and it deliberately does NOT rewrite the parser: the
// existing single-character triggers keep working exactly as before, and a value that is already saved
// keeps working until the user changes it (the settings surface reports it instead).
public static class SearchSyntaxReserved
{
    // The character that starts a plugin query token. It is listed among the reserved characters because
    // the scanner reads it as one -- but only because it IS the token prefix: QueryTokenScanner is handed
    // GlobalTokenPrefix, so this is the one character whose meaning the prefix fields themselves decide.
    // See IsUnusableAsTokenPrefix for what follows from that.
    public const char TokenPrefixCharacter = '\\';

    // The character that inverts a term's precision. It is the second of the two triggers a term's first
    // character can carry (see TermTriggers), and it is listed here for the same reason as the rest: it is
    // consumed before any first-character trigger is consulted. Choosing it as a token prefix would shadow
    // the inversion syntax for every word carrying that prefix, silently.
    public const char PrecisionInversionCharacter = '?';

    // Claimed at the start of a query by the search syntax itself:
    //   '\\'  the plugin query token prefix (GlobalTokenPrefix, itself configurable)
    //   '<' '>' the sort/filter token triggers, which QueryTokenScanner always recognizes
    //   ':'   the exclusion operator, and the drive spec's separator
    //   '*'   the one-search exclusion bypass, read from the first character only
    //   '/'   the regex clause delimiter
    //   '?'   the precision-inversion trigger, read from the first character of any word
    //
    // '/' is the only entry that is not a whole-query trigger: it opens a clause only when the same word
    // also closes with one, so "/mnt/c" is ordinary text. It is listed anyway because QueryTokenScanner
    // compares a prefix against the first character ALONE, so a '/' prefix claims the entire clause as a
    // token and regex queries stop filtering with nothing on screen to explain it -- the exact failure this
    // list exists to catch. See QueryTokenScannerTests.Scan_SlashPrefix_EatsTheRegexClause.
    //
    // '?' is the same kind of entry from the other end: it is read per WORD rather than per query, so a
    // '?' token prefix would not break tokenization at all -- it would quietly retire the inversion syntax
    // for every word the prefix lifts, which is why it is refused here rather than left to be discovered.
    public static IReadOnlyList<char> LeadingCharacters { get; } = new[] { TokenPrefixCharacter, '<', '>', ':', '*', '/', PrecisionInversionCharacter };

    // Leading characters an instant-answer provider claims for itself, by its own hardcoded rule rather
    // than by the search syntax. They are not syntax, so they are listed separately -- but they are just
    // as unavailable to another first-character trigger, which is the whole point of ValidateLeadingCharacter.
    //
    //   '#'   CommandInstantProvider: an elevated shell command ("#dir")
    //   '$'   CommandInstantProvider: a normal shell command ("$dir")
    //   '%'   EnvironmentVariableInstantProvider: fuzzes environment variable names ("%TE")
    //
    // Providers matching on a configurable KEYWORD are deliberately not here: their keyword is the user's
    // own setting, so the collision is the user's to see and fix, and the settings page already reports it.
    public static IReadOnlyList<char> ProviderClaimedCharacters { get; } = new[] { '#', '$', '%' };

    // True when `value` starts with a character the search syntax consumes before any plugin or trigger
    // ever sees the query.
    public static bool IsReserved(char value) => LeadingCharacters.Contains(value);

    /// <summary>
    /// True when this character cannot be the plugin query token prefix. Narrower than
    /// <see cref="IsReserved"/> by exactly one character, and for a real reason: the token prefix is not
    /// reserved against itself. QueryTokenScanner reads '\' as a token start only because it was handed
    /// '\' as the prefix, so a prefix field set to '\' is what makes those tokens work -- while every other
    /// character in that list is consumed by the scanner or the parser no matter what the prefix says.
    /// </summary>
    public static bool IsUnusableAsTokenPrefix(char value)
        => value != TokenPrefixCharacter && IsReserved(value);

    /// <summary>
    /// Why this value cannot be used as a leading trigger, or null when it is usable. Shared by every
    /// surface so the same input always gets the same answer. Only the FIRST character is judged: every
    /// trigger is matched against the start of the query, so that is the only position the search syntax
    /// and the providers compete for, and a keyword is free to contain anything else.
    /// </summary>
    public static string? ValidateLeadingCharacter(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return TranslationManager.Instance["General_ReservedCharacterEmpty"];

        var first = value[0];
        if (LeadingCharacters.Contains(first))
            return string.Format(
                TranslationManager.Instance["General_ReservedCharacterSyntax"], DescribeLeadingCharacters());

        // Claimed by a provider's own hardcoded rule rather than by the syntax. The message names the
        // characters, because "you cannot use #, $ or %" is arbitrary unless the user is told what already
        // answers to them.
        return ProviderClaimedCharacters.Contains(first)
            ? TranslationManager.Instance["General_ReservedCharacterProvider"]
            : null;
    }

    // The characters listed for a message, so the warning can name them without each locale hardcoding a
    // translated copy that can drift from the list above. Interpolated into General_ReservedCharacterSyntax
    // as {0}; a locale whose text lost the placeholder still renders, just without the list.
    public static string DescribeLeadingCharacters() => string.Join(' ', LeadingCharacters);
}
