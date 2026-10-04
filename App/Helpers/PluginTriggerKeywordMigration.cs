using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Helpers;

// Clears an instant-answer trigger keyword a plugin persisted with a leading character that QueryTokenScanner
// lifts the whole word away for.
//
// Why this cannot live in Core with the other legacy checks: which plugin settings ARE trigger keywords is
// only knowable from each plugin's config schema (ConfigFieldValidation.TriggerKeyword), and Core cannot see
// plugin assemblies. So the schema walk happens here, in the App, and only the DECISION is pure -- the
// candidate fields are handed in, which is what makes the rule testable without loading a single plugin.
//
// What actually makes such a keyword unusable is being lifted out of the query before the file search runs:
// the scanner treats any word opening with the configured token prefix, or with '<' or '>', as a token, and a
// token no provider claims empties the file result list (see QueryTokenDispatcher). The keyword still reaches
// instant providers, which are handed the untouched box text, so this is "your file search goes blank every
// time you use it" rather than "the trigger never fires".
//
// It used to claim the precision-inversion character for that list, which is wrong on two counts: '?' is not
// one of the characters the scanner lifts, and PluginTriggerQuery.Strip removes an invoked trigger word from
// the file-search text anyway -- so a '?' keyword worked, and wiping it destroyed a working setting while the
// balloon gave the user a reason that did not hold.
internal static class PluginTriggerKeywordMigration
{
    /// <summary>
    /// Clears every trigger-keyword setting whose stored value opens with a character the scanner lifts the
    /// word away for, returning what was cleared so the caller can say so. Mutates: the caller has to save
    /// the settings.
    /// </summary>
    /// <remarks>
    /// Idempotent (the removed key is what makes it unrepeatable), and driven by the schema rather than by a
    /// list of plugin ids, so a third-party plugin declaring a trigger keyword is covered without being
    /// named here.
    /// </remarks>
    internal static List<(string PluginId, string PluginName, string Key, string Value)> TakeUnusable(
        UserSettings settings,
        IEnumerable<(string PluginId, string PluginName, PluginConfigField Field)> candidates)
    {
        var cleared = new List<(string, string, string, string)>();
        var lifted = LiftedLeadingCharacters(settings);

        foreach (var (pluginId, pluginName, field) in candidates)
        {
            if (field.Validation != ConfigFieldValidation.TriggerKeyword || string.IsNullOrEmpty(field.Key))
                continue;

            var value = settings.GetPluginSetting<string?>(pluginId, field.Key, null);
            if (value is not { Length: > 0 } || !lifted.Contains(value[0]))
                continue;

            settings.SetPluginSetting(pluginId, field.Key, null);
            cleared.Add((pluginId, pluginName, field.Key, value));
        }

        return cleared;
    }

    // The set QueryTokenScanner.IsToken lifts by: the configured token prefix (blank means the shipped
    // default) and the two sort/filter triggers, which are hardcoded in the scanner rather than
    // configurable, so they are spelled out here to match it. Read from the caller's settings instance so
    // the rule stays a pure function of (settings, candidates).
    private static HashSet<char> LiftedLeadingCharacters(UserSettings settings)
    {
        var prefix = settings.GlobalTokenPrefix;
        return new HashSet<char>
        {
            string.IsNullOrEmpty(prefix) ? GlobalTokenPrefix.Default : prefix[0],
            '<',
            '>',
        };
    }

    /// <summary>
    /// Every loaded plugin's declared config fields, flattened out of their groups, paired with the plugin id
    /// (its DLL name, the key its settings are stored under) and its display name.
    /// </summary>
    /// <remarks>
    /// The untestable half, deliberately kept apart from the rule above: it needs the loaded plugin
    /// assemblies. A plugin whose schema cannot be built is skipped rather than failing the migration --
    /// PluginLoaderHelper.ResolveConfigFields already absorbs that and returns an empty list.
    /// </remarks>
    internal static IEnumerable<(string PluginId, string PluginName, PluginConfigField Field)> Candidates()
    {
        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
            yield break;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic
                && a.Location.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(a.Location).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var assembly in assemblies)
        {
            var pluginId = Path.GetFileNameWithoutExtension(assembly.Location);
            var pluginName = PluginLoaderHelper.GetPluginDisplayName(assembly, PluginManager.Instance);

            foreach (var field in Flatten(PluginLoaderHelper.ResolveConfigFields(assembly)))
                yield return (pluginId, pluginName, field);
        }
    }

    // A trigger keyword can sit at the top level of a schema or inside a Group; the array/object shapes carry
    // their own SubFields as the shape of one stored value, which is not a keyword setting in its own right.
    private static IEnumerable<PluginConfigField> Flatten(IEnumerable<PluginConfigField> fields)
    {
        foreach (var field in fields)
        {
            if (field.FieldType == ConfigFieldType.Group && field.SubFields is { Count: > 0 } subFields)
            {
                foreach (var nested in Flatten(subFields))
                    yield return nested;
                continue;
            }

            yield return field;
        }
    }
}
