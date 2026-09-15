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
// There are two real collisions today, both against the app-wide GlobalTokenPrefix:
//
//   1. a plugin that declares its own prefix (CoreExtensions' CustomFilterPrefix) -- the two must differ,
//      since a token can only carry one leading character;
//   2. the fixed sort/filter triggers '<' and '>', which the scanner always treats as token starts
//      regardless of what GlobalTokenPrefix says. Setting the global prefix to one of them would leave
//      the plugin tokens unreachable, because QueryTokenScanner pulls those words out as sort/filter
//      tokens before any plugin ever sees them.
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
    // exclusion, bypass and quote characters. The two are used for different sentences: a prefix equal to
    // '<'/'>' leaves the plugin tokens permanently unreachable, while a prefix equal to ':' still leaves
    // them reachable but collides with exclusion syntax.
    internal static bool IsAlwaysTokenTrigger(char prefix) => prefix == '<' || prefix == '>';

    /// <summary>The conflict to show under the app-wide prefix field, or null when it is usable.</summary>
    public static string? GlobalPrefixConflict(string? globalPrefix, UserSettings settings)
    {
        if (string.IsNullOrEmpty(globalPrefix))
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictEmpty"];

        var prefix = globalPrefix[0];
        // Any character the search syntax owns is unusable here, not just the always-on sort/filter pair:
        // a ':' prefix collides with exclusion syntax and a '*' prefix with the exclusion bypass, and the
        // settings field is the place to say so. This deliberately subsumes the '<'/'>' case, which is the
        // strictly worse variant of the same mistake.
        if (SearchSyntaxReserved.IsReserved(prefix))
            return TranslationManager.Instance["General_GlobalTokenPrefixConflictReserved"];

        return OwnedByPlugin(prefix, settings, null, null)
            ? TranslationManager.Instance["General_GlobalTokenPrefixConflictPlugin"]
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
    public static string? PluginPrefixConflict(string pluginId, PluginConfigField ownField, string? pluginPrefix, UserSettings settings)
    {
        if (string.IsNullOrEmpty(pluginPrefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictEmpty"];

        var prefix = pluginPrefix[0];
        if (IsAlwaysTokenTrigger(prefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictReserved"];

        if (SearchSyntaxReserved.IsReserved(prefix))
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictSyntax"];

        if (settings.GlobalTokenPrefix is { Length: > 0 } global && global[0] == prefix)
            return TranslationManager.Instance["Plugins_QueryTokenPrefixConflictMain"];

        return OwnedByPlugin(prefix, settings, pluginId, ownField.Key)
            ? TranslationManager.Instance["Plugins_QueryTokenPrefixConflictPlugin"]
            : null;
    }

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
            var dllName = Path.GetFileName(assembly.Location);
            if (dllName.Equals("Lertaro.PluginSdk.dll", StringComparison.OrdinalIgnoreCase))
                continue;

            List<PluginConfigField>? fields = null;
            try
            {
                fields = PluginLoaderHelper.ResolveConfigFields(assembly);
            }
            catch
            {
                // A plugin whose schema cannot be built simply does not take part in the check.
            }

            if (fields is { Count: > 0 })
                yield return (Path.GetFileNameWithoutExtension(dllName), fields);
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
