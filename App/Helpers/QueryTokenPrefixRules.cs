using System.IO;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Helpers;

// Prefix collision rules for the query-token trigger character.
//
// Two different prefixes can end up claiming the same character, and the token scanner resolves that by
// whichever provider is asked first -- i.e. silently, in an order no user can see. The failure is
// invisible rather than loud: "\audio" simply stops filtering and nobody can tell whether the rule, the
// spelling, or the prefix is at fault. So the settings surface the collision instead of ordering it.
//
// There are two real collisions:
//
//   1. the search syntax, which consumes '<' '>' ':' '*' '/' and '?' at the start of a query whatever the
//      configured prefix is -- the sort/filter pair worst of all, since QueryTokenScanner pulls those
//      words out before any plugin sees them, leaving the plugin tokens permanently unreachable;
//   2. a SECOND plugin declaring a prefix that a first one already answers to.
//
// Note what is NOT a collision: a plugin's prefix matching the app-wide GlobalTokenPrefix. The scanner
// lifts a token by the global prefix and hands it to the provider with that character still on the front
// (QueryTokenScanner.Scan(query, GlobalTokenPrefix)), so a provider only ever claims tokens whose leading
// character equals its own configured prefix -- the two matching is what makes the feature work, and a
// mismatch is what breaks it (the token is lifted out of the query and then claimed by nobody, which
// QueryTokenDispatcher answers with an empty result list). Reporting the match as a collision (as this
// file used to) flagged the shipped defaults, where both are '\'.
//
// The plugin prefixes are found by walking each loaded plugin's config SCHEMA rather than by referencing
// the plugin: a Text field whose MaxLength is 1 IS a single-character trigger, which is the only reason
// such a field exists. That keeps this file free of any dependency on a specific plugin (the App loads
// plugins, never the other way round) and lets a future plugin join the check by declaring the same
// shape. ponytail: single-character prefixes only -- the token scanner only ever compares one character,
// so a multi-character prefix would not work anyway.
//
// Both checks take the value being EDITED, not the saved one, and exclude the field being edited from the
// scan: the settings pages stage edits until Apply and re-validate on every keystroke, so a field must
// never be reported as conflicting with its own previous value.
public static class QueryTokenPrefixRules
{
    // True for the characters QueryTokenScanner always reads as token starts whatever the configured
    // prefix is -- only the sort/filter pair, unlike SearchSyntaxReserved.IsReserved, which also covers the
    // exclusion, bypass, token-prefix, regex-delimiter and precision-inversion characters. The two are used
    // for different sentences: a prefix equal to '<'/'>' leaves the plugin tokens permanently unreachable,
    // while a prefix equal to ':' or '/' still leaves them reachable but collides with the exclusion
    // operator or the regex clause delimiter.
    internal static bool IsAlwaysTokenTrigger(char prefix) => prefix == '<' || prefix == '>';

    /// <summary>The conflict to show under the app-wide prefix field, or null when it is usable.</summary>
    /// <remarks>
    /// Only the search syntax is consulted -- the loaded plugins are deliberately NOT. This field and a
    /// plugin's own prefix field hold the same character by design: QueryTokenScanner hands the provider
    /// the token INCLUDING the global prefix, so a provider only ever claims the tokens whose leading
    /// character matches its configured prefix, and a plugin whose prefix differs from this one can never
    /// be reached. "A plugin already uses this character" is therefore the working configuration, not a
    /// collision -- see PluginPrefixConflict, which reports the one collision that is real (a SECOND
    /// plugin claiming a character the first one already answers to).
    /// </remarks>
    public static string? GlobalPrefixConflict(string? globalPrefix)
    {
        if (string.IsNullOrEmpty(globalPrefix))
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictEmpty"];

        // Any character the search syntax owns is unusable here, not just the always-on sort/filter pair:
        // a ':' prefix collides with exclusion syntax, a '*' prefix with the exclusion bypass and a '/'
        // prefix with the regex clause delimiter, and the settings field is the place to say so. This
        // deliberately subsumes the '<'/'>' case, which is the strictly worse variant of the same mistake.
        //
        // The message interpolates the set that actually applies rather than naming characters itself, so
        // it stays true as the syntax grows -- it used to name only '<'/'>', which mis-explained ':' '*'
        // and '/', and it used to interpolate the FULL reserved list, which told a user whose ':' was
        // refused that '\' is reserved too -- under a field whose default IS '\'.
        //
        // '\' is deliberately NOT one of them: it is this field's own character (see
        // SearchSyntaxReserved.IsUnusableAsTokenPrefix), so the shipped default reports nothing.
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
    /// The conflict to show under a plugin's own prefix field, or null when it is usable. The field's own
    /// plugin is excluded from the scan, so re-validating an unchanged value never conflicts with itself.
    /// </summary>
    /// <remarks>
    /// A value that matches the app-wide prefix is accepted, because that is the value that works: the
    /// scanner lifts tokens by the global prefix and hands them to the provider with that character still
    /// on the front, so a plugin prefix that differed would simply never be asked (see
    /// GlobalPrefixConflict). Only a SECOND plugin claiming a character another one already answers to is
    /// reported -- the dispatcher resolves that by whichever provider is asked first.
    /// </remarks>
    public static string? PluginPrefixConflict(string pluginId, PluginConfigField ownField, string? pluginPrefix, UserSettings settings)
    {
        if (string.IsNullOrEmpty(pluginPrefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictEmpty"];

        var prefix = pluginPrefix[0];
        if (IsAlwaysTokenTrigger(prefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictReserved"];

        if (SearchSyntaxReserved.IsUnusableAsTokenPrefix(prefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictSyntax"];

        return OwnedByPlugin(prefix, settings, pluginId, ownField.Key)
            ? TranslationManager.Instance["Plugins_QueryTokenPrefixConflictPlugin"]
            : null;
    }

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

    /// <summary>
    /// True for the one-character Text fields that declare a query-token trigger -- see this class's
    /// header for why the schema shape is the criterion rather than a naming convention. Public because
    /// the field view model asks the same question to decide whether the warning applies to it at all.
    /// </summary>
    public static bool IsPrefixField(PluginConfigField field)
        => field.FieldType == ConfigFieldType.Text && field.MaxLength == 1;

    /// <summary>
    /// The first provider already claiming this character, or null when the prefix is free. Pure on
    /// purpose: the candidate providers are handed in, so the collision DECISION is testable without
    /// loading a single plugin (the schema walk that produces the candidates is the untestable half -- see
    /// EnumeratePluginSchemas). <paramref name="excludedPluginId"/> /
    /// <paramref name="excludedFieldKey"/> name the field being edited, which must not conflict with
    /// itself.
    /// </summary>
    internal static PrefixOwner? FindOwner(char prefix, IEnumerable<PrefixOwner> candidates, string? excludedPluginId, string? excludedFieldKey)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.IsSameFieldAs(excludedPluginId, excludedFieldKey))
                continue;
            if (candidate.Value.Length > 0 && candidate.Value[0] == prefix)
                return candidate;
        }

        return null;
    }

    // Walks every loaded plugin's schema for a prefix field already using this character. The excluded
    // pair (one plugin's one field) is skipped because it is the field being validated.
    private static bool OwnedByPlugin(char prefix, UserSettings settings, string? excludedPluginId, string? excludedFieldKey)
    {
        var candidates = EnumeratePrefixFields()
            .Select(entry => new PrefixOwner(entry.PluginId, entry.Field.Key, ConfiguredValue(entry.PluginId, entry.Field, settings)));

        return FindOwner(prefix, candidates, excludedPluginId, excludedFieldKey) != null;
    }

    private static string ConfiguredValue(string pluginId, PluginConfigField field, UserSettings settings)
    {
        var configured = settings.GetPluginSetting<string?>(pluginId, field.Key, null);
        return string.IsNullOrEmpty(configured) ? field.DefaultValue as string ?? string.Empty : configured;
    }

    private static IEnumerable<(string PluginId, PluginConfigField Field)> EnumeratePrefixFields()
    {
        foreach (var (pluginId, fields) in EnumeratePluginSchemas())
        {
            foreach (var field in fields)
            {
                if (IsPrefixField(field))
                    yield return (pluginId, field);
            }
        }
    }

    // Plugin assemblies only (Lertaro.Plugins.*.dll), the same set PluginLoaderHelper.BuildSchemaDefaultsMap
    // walks -- a dependency DLL sitting in the same folder declares no schema and is skipped either way.
    private static IEnumerable<(string PluginId, List<PluginConfigField> Fields)> EnumeratePluginSchemas()
    {
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
            yield break;

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(a.Location).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var assembly in loadedAssemblies)
        {
            // ResolveConfigFields already absorbs a plugin whose schema cannot be built (it returns an
            // empty list), so a schema failure here simply means the plugin does not take part in the check.
            var fields = PluginLoaderHelper.ResolveConfigFields(assembly);

            if (fields is { Count: > 0 })
                yield return (Path.GetFileNameWithoutExtension(assembly.Location), fields);
        }
    }
}

/// <summary>One query-token prefix a plugin declares, paired with the character it currently resolves to.</summary>
/// <param name="PluginId">The declaring plugin, as its DLL name without extension.</param>
/// <param name="FieldKey">The config field that holds it.</param>
/// <param name="Value">The configured value, or the schema default when nothing is persisted yet.</param>
internal readonly record struct PrefixOwner(string PluginId, string FieldKey, string Value)
{
    // Identity is (plugin, field key), never the field object: a plugin builds a fresh schema on every
    // GetConfigSchema() call, so reference equality would never recognize the field being edited.
    public bool IsSameFieldAs(string? pluginId, string? fieldKey)
        => pluginId != null && fieldKey != null
            && PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase)
            && FieldKey.Equals(fieldKey, StringComparison.OrdinalIgnoreCase);
}
