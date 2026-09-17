using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Helpers;

// Clears an instant-answer trigger keyword a plugin persisted before '?' became the precision-inversion
// trigger.
//
// Why this cannot live in Core with the other legacy checks: which plugin settings ARE trigger keywords is
// only knowable from each plugin's config schema (ConfigFieldValidation.TriggerKeyword), and Core cannot see
// plugin assemblies. So the schema walk happens here, in the App, and only the DECISION is pure -- the
// candidate fields are handed in, which is what makes the rule testable without loading a single plugin.
//
// A keyword starting with '?' is dead, not merely misread: the parser strips the '?' as the precision trigger
// before any provider is asked, so the trigger can never fire. Clearing the stored value is therefore not a
// loss -- the plugin falls back to the keyword its own schema ships, which works.
internal static class PluginTriggerKeywordMigration
{
    /// <summary>
    /// Clears every trigger-keyword setting whose stored value starts with the precision-inversion character,
    /// returning what was cleared so the caller can say so. Mutates: the caller has to save the settings.
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

        foreach (var (pluginId, pluginName, field) in candidates)
        {
            if (field.Validation != ConfigFieldValidation.TriggerKeyword || string.IsNullOrEmpty(field.Key))
                continue;

            var value = settings.GetPluginSetting<string?>(pluginId, field.Key, null);
            if (value is not { Length: > 0 } || value[0] != SearchSyntaxReserved.PrecisionInversionCharacter)
                continue;

            settings.SetPluginSetting(pluginId, field.Key, null);
            cleared.Add((pluginId, pluginName, field.Key, value));
        }

        return cleared;
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
