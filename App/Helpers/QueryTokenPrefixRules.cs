using Lertaro.App.Services;

namespace Lertaro.App.Helpers;

// What the query-token trigger character may be, and whether an unusable value merely warns or also refuses
// to save.
//
// There is exactly ONE token prefix now, the app-wide GlobalTokenPrefix, and every plugin reads it back
// through the SDK (PluginSdk.Services.SearchSyntaxService) instead of storing a copy of its own. So the only
// collision left is with the search syntax itself.
//
// There used to be a second check here -- "another plugin already answers to this character". It was removed
// rather than kept for a plugin that might want its own: QueryTokenScanner only ever lifts a word by the
// GLOBAL prefix and hands the token on with that character still attached, so a provider's own value can
// only ever agree with it or claim nothing -- and the check fired precisely on the value that agrees, i.e.
// on the only configuration that works. Keeping it would have been a trap rather than a safeguard.
public static class QueryTokenPrefixRules
{
    /// <summary>The conflict to show under the app-wide prefix field, or null when it is usable.</summary>
    /// <remarks>
    /// Any character the search syntax owns is unusable here, not just the always-on sort/filter pair
    /// ('&lt;' '&gt;', the worst case, because QueryTokenScanner reads those as token starts whatever the
    /// prefix says and the plugin tokens become permanently unreachable): a ':' prefix collides with
    /// exclusion syntax, a '*' prefix with the exclusion bypass, a '/' prefix with the regex clause
    /// delimiter, and a '?' prefix would silently retire the precision-inversion trigger for every word it
    /// lifts.
    ///
    /// The message interpolates the set that actually applies rather than naming characters itself, so it
    /// stays true as the syntax grows -- it used to name only '&lt;'/'&gt;', which mis-explained ':' '*'
    /// and '/', and it used to interpolate the FULL reserved list, which told a user whose ':' was refused
    /// that '\' is reserved too -- under a field whose default IS '\'.
    ///
    /// '\' is deliberately NOT one of them: it is this field's own character (see
    /// SearchSyntaxReserved.IsUnusableAsTokenPrefix), so the shipped default reports nothing.
    /// </remarks>
    public static string? GlobalPrefixConflict(string? globalPrefix)
    {
        if (string.IsNullOrEmpty(globalPrefix))
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictEmpty"];

        return SearchSyntaxReserved.IsUnusableAsTokenPrefix(globalPrefix[0])
            ? string.Format(
                TranslationManager.Instance["General_GlobalTokenPrefixConflictReserved"],
                SearchSyntaxReserved.DescribeUnusableTokenPrefixCharacters())
            : null;
    }

    /// <summary>
    /// The conflict to show under an instant-answer trigger keyword field, or null when it is usable.
    /// Providers recognize their keyword as a prefix of the whole query, so a keyword that starts with a
    /// character the search syntax consumes is stripped before the provider is ever asked -- the trigger
    /// silently does nothing. The shared rule lives in SearchSyntaxReserved so this and the prefix check
    /// cannot drift apart.
    /// </summary>
    public static string? TriggerKeywordConflict(string? keyword) => SearchSyntaxReserved.ValidateLeadingCharacter(keyword);

    /// <summary>
    /// Whether an error a page is showing next to a prefix/trigger field should also STOP the whole
    /// settings window from saving, or only be reported.
    /// </summary>
    /// <remarks>
    /// The distinction is between a value the user is entering right now and one that is merely already in
    /// effect (persisted by an earlier release, or resolved from the applier's own blank-to-default rule).
    /// Saving a value the user just typed that visibly cannot work is worth refusing -- the previous,
    /// working value would otherwise be replaced by one that never fires. Refusing on the carried-over
    /// value instead locks the user out of saving EVERY other setting in the window, which is why this
    /// release ships a startup notice for exactly that value (see LegacySettingsAdvisor): the field keeps
    /// showing the warning, and the user can still save everything else. An empty value never blocks
    /// either, because GeneralSettingsApplier resolves a blank prefix to the default rather than rejecting
    /// it.
    /// </remarks>
    /// <param name="typedValue">The value the field currently holds.</param>
    /// <param name="savedValue">The value already persisted, or null when nothing has been persisted yet.</param>
    internal static bool BlocksSaving(string? typedValue, string? savedValue)
        => !string.IsNullOrEmpty(typedValue) && !string.Equals(typedValue, savedValue, StringComparison.Ordinal);
}
