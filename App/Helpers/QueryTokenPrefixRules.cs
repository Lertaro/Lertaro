using Lertaro.App.Services;

namespace Lertaro.App.Helpers;

// What the query-token trigger character may be, and whether an unusable value merely warns or also refuses
// to save.
//
// There is exactly one token prefix, the app-wide GlobalTokenPrefix, and every plugin reads it back through
// the SDK (PluginSdk.Services.SearchSyntaxService) instead of storing a copy. Syntax, built-in providers
// and configured trigger words must leave this character available.
//
// A second check, "another plugin already answers to this character", was removed: the scanner lifts a word
// by the GLOBAL prefix and hands the token over with that character still attached, so a provider's own
// value can only agree with it or claim nothing -- and the check fired on the agreeing value, which is the
// only one that works.
public static class QueryTokenPrefixRules
{
    /// <summary>The prefix the search actually uses: the saved one, or the default when it cannot work.</summary>
    internal static char PrefixFor(Core.UserSettings settings) => GlobalTokenPrefix.For(settings);
    /// <summary>The conflict to show under the app-wide prefix field, or null when it is usable.</summary>
    /// <remarks>
    /// The prefix must be a character a file name cannot contain -- the scanner lifts a whole word that opens
    /// with it, so any other character turns real searches into tokens nobody claims (see
    /// SearchSyntaxReserved.IsUsableTokenPrefix). The search syntax owns every illegal character but '\', so
    /// '\' is the only value that passes; everything else is refused rather than warned about. The refusal is
    /// what makes the value keep working: a ':' prefix would otherwise collide with exclusion syntax, a '*'
    /// prefix with the exclusion bypass, a '/' prefix with the regex delimiter and a '?' prefix would retire
    /// the precision-inversion trigger.
    ///
    /// Two messages, because the two failures are different facts: a character the syntax owns is named
    /// against the set that applies (so the message stays true as the syntax grows), and anything else is told
    /// what a prefix has to be.
    /// </remarks>
    public static string? GlobalPrefixConflict(string? globalPrefix, IEnumerable<string>? otherTriggers = null)
    {
        if (string.IsNullOrEmpty(globalPrefix))
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictEmpty"];

        if (globalPrefix.Length != 1)
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictEmpty"];

        var character = globalPrefix[0];
        if (!SearchSyntaxReserved.IsUsableTokenPrefix(character))
            return SearchSyntaxReserved.UnusableTokenPrefixCharacters.Contains(character)
                ? string.Format(
                    TranslationManager.Instance["General_GlobalTokenPrefixConflictReserved"],
                    SearchSyntaxReserved.DescribeUnusableTokenPrefixCharacters())
                : TranslationManager.Instance["General_GlobalTokenPrefixConflictFileName"];

        otherTriggers ??= ViewModels.Search.Dispatch.PluginTriggerQuery.Collect().Select(entry => entry.Word);
        return otherTriggers.Any(trigger => !string.IsNullOrEmpty(trigger)
            && char.ToUpperInvariant(trigger[0]) == char.ToUpperInvariant(character))
            ? TranslationManager.Instance["General_GlobalTokenPrefixConflictConfigured"] : null;
    }

    /// <summary>
    /// The conflict to show under an instant-answer trigger keyword field, or null when it is usable.
    /// Providers recognize their keyword as a prefix of the whole query, so a keyword that starts with a
    /// character the search syntax consumes is stripped before the provider is ever asked -- the trigger
    /// silently does nothing. The shared rule lives in SearchSyntaxReserved so this and the prefix check
    /// cannot drift apart.
    /// </summary>
    public static string? TriggerKeywordConflict(string? keyword, char? prefix = null) => SearchSyntaxReserved.ValidateLeadingCharacter(keyword, prefix);

    /// <summary>
    /// Whether an error a page is showing next to a prefix/trigger field should also STOP the whole
    /// settings window from saving, or only be reported.
    /// </summary>
    /// <remarks>
    /// A value the user just typed is worth refusing, since otherwise the working value is replaced by one
    /// that never fires. A value merely carried over from an earlier release must not block saving: that
    /// locks the user out of every other setting in the window, so the field keeps warning and the startup
    /// notice reports it (see LegacySettingsAdvisor). A blank prefix never blocks either, because
    /// GeneralSettingsApplier resolves it to the default.
    /// </remarks>
    /// <param name="typedValue">The value the field currently holds.</param>
    /// <param name="savedValue">The value already persisted, or null when nothing has been persisted yet.</param>
    internal static bool BlocksSaving(string? typedValue, string? savedValue)
        => !string.IsNullOrEmpty(typedValue) && !string.Equals(typedValue, savedValue, StringComparison.Ordinal);
}
