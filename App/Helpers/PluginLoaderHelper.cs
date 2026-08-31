using System.IO;
using System.Reflection;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Registries;

using Lertaro.App.Services.Plugin;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.Helpers;

public static class PluginLoaderHelper
{
    public static List<PluginInfoViewModel> BuildPluginList(UserSettings userSettings)
    {
        var result = new List<PluginInfoViewModel>();
        var manager = PluginManager.Instance;
        var disabledSet = new HashSet<string>(userSettings.DisabledPluginComponents);

        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
        {
            return result;
        }

        // Only actual plugin entry assemblies (Lertaro.Plugins.*.dll) are processed; dependency DLLs are
        // resolved implicitly from the plugin's own directory and are not plugin candidates.
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(a.Location).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var assembly in loadedAssemblies)
        {
            var dllName = Path.GetFileName(assembly.Location);


            var sdkVersion = "1.0.0";
            var referencedSdk = assembly.GetReferencedAssemblies()
                .FirstOrDefault(r => r.Name != null && r.Name.Equals("PluginSdk", StringComparison.OrdinalIgnoreCase));
            if (referencedSdk != null && referencedSdk.Version != null)
            {
                sdkVersion = referencedSdk.Version.ToString(3);
            }

            var pluginName = GetPluginDisplayName(assembly, manager, out var pluginInstance);
            var pluginVersion = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

            var components = new List<PluginComponentViewModel>();
            if (pluginInstance != null)
            {
                components = PluginComponentBuilder.BuildComponents(pluginInstance, dllName, manager, disabledSet);
            }
            else
            {
                PluginComponentBuilder.AddAssemblyProviders(components, assembly, dllName, manager, disabledSet);
            }

            var configFields = new List<PluginConfigFieldViewModel>();
            var schema = TryLoadConfigFields(assembly, dllName, pluginInstance, userSettings, configFields);

            // Skip assemblies that registered nothing at all -- a plugin's own bundled dependency DLL
            // (e.g. Microsoft.Data.Sqlite.dll, SQLitePCLRaw.core.dll) now sits alongside it in its own
            // Plugins/ subdirectory and gets loaded like any other .NET assembly there, but it never
            // implements IPlugin/IConfigurable or registers any provider, so it has nothing to show or
            // toggle here. Showing an empty card per dependency would just be noise.
            if (components.Count == 0 && configFields.Count == 0)
                continue;

            var description = pluginInstance != null ? PluginComponentBuilder.GetDescriptionWithFallback(pluginInstance) : string.Empty;
            var websiteUrl = pluginInstance?.WebsiteUrl;
            var websiteLabel = pluginInstance?.WebsiteLabel;
            result.Add(new PluginInfoViewModel(pluginName, pluginVersion, dllName, sdkVersion, components, configFields, description, schema?.OnSave, schema?.OnRollback, websiteUrl, websiteLabel));
        }

        // Sorted here rather than at the one list that displays it, so the settings search index, which
        // builds from this same call, offers plugins in the order the page will show them.
        return SortForDisplay(result);
    }

    /// <summary>Final display order of the plugin list: fully-disabled plugins sink to the end
    /// (they are the least actionable), then the actionable-first DisplayRank bands, then name.</summary>
    internal static List<PluginInfoViewModel> SortForDisplay(IEnumerable<PluginInfoViewModel> plugins) => plugins
        .OrderBy(p => p.IsFullyDisabled)
        .ThenBy(p => DisplayRank(p.HasConfigFields, p.RawComponents.Any(c => c.IsToggleable)))
        .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>Which band a plugin sorts into: what you can act on first, alphabetical within each.</summary>
    /// <remarks>
    /// Deliberately not PluginInfoViewModel.HasToggleableComponents, which is "more than one" because it
    /// gates the Select All link. A plugin with exactly one switch is still a plugin with something to
    /// switch, and reusing that property would have filed it under "nothing to do here".
    /// </remarks>
    internal static int DisplayRank(bool hasConfigFields, bool hasAnyToggleableComponent) =>
        hasConfigFields ? 0
        : hasAnyToggleableComponent ? 1
        : 2;

    /// <summary>Resolves the display name shown for a plugin -- Plugin Management's card header, and
    /// any other UI that groups components by their owning plugin (e.g. the plugin page's own component
    /// list). Prefers the IPlugin's own Name if this assembly has one, else the first named provider
    /// found via FallbackPluginName, else the DLL's bare filename.</summary>
    public static string GetPluginDisplayName(Assembly assembly, PluginManager manager) => GetPluginDisplayName(assembly, manager, out _);

    private static string GetPluginDisplayName(Assembly assembly, PluginManager manager, out IPlugin? pluginInstance)
    {
        var defaultName = Path.GetFileNameWithoutExtension(assembly.Location);
        var pluginType = assembly.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        pluginInstance = pluginType != null ? manager.Plugins.FirstOrDefault(p => p.GetType() == pluginType) : null;
        return pluginInstance != null ? pluginInstance.Name : FallbackPluginName(assembly, defaultName);
    }

    private static string FallbackPluginName(Assembly assembly, string defaultName)
    {
        var firstAliasProv = AliasProviderRegistry.GetAllProviders().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstAliasProv != null && !string.IsNullOrWhiteSpace(firstAliasProv.Name)) return firstAliasProv.Name;

        var firstPathCol = ActivePathCollectorRegistry.GetAllCollectors().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstPathCol != null && !string.IsNullOrWhiteSpace(firstPathCol.Name)) return firstPathCol.Name;

        var firstAdapter = FileDialogAdapterRegistry.GetAllAdapters().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstAdapter != null && !string.IsNullOrWhiteSpace(firstAdapter.Name)) return firstAdapter.Name;

        var firstInlineAdapter = InlineSearchAdapterRegistry.GetAllAdapters().FirstOrDefault(p => p.GetType().Assembly == assembly);
        if (firstInlineAdapter != null && !string.IsNullOrWhiteSpace(firstInlineAdapter.Name)) return firstInlineAdapter.Name;

        return defaultName;
    }

    private static PluginConfigSchema? TryLoadConfigFields(Assembly assembly, string dllName, IPlugin? pluginInstance, UserSettings userSettings, List<PluginConfigFieldViewModel> configFields)
    {
        try
        {
            var configurableInstance = ResolveConfigurable(assembly, pluginInstance);
            if (configurableInstance != null)
            {
                var schema = configurableInstance.GetConfigSchema();
                if (schema != null && schema.Fields != null)
                {
                    var pluginId = Path.GetFileNameWithoutExtension(dllName);
                    foreach (var field in schema.Fields)
                    {
                        configFields.Add(new PluginConfigFieldViewModel(pluginId, field, userSettings));
                    }
                    return schema;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Finds this assembly's IConfigurable component (at most one is currently supported per
    /// assembly), reusing the plugin instance if it implements IConfigurable itself, else creating a
    /// throwaway instance just to call GetConfigSchema().</summary>
    private static IConfigurable? ResolveConfigurable(Assembly assembly, IPlugin? pluginInstance)
    {
        var configurableType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IConfigurable).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        if (configurableType == null)
            return null;

        if (pluginInstance != null && configurableType.IsAssignableFrom(pluginInstance.GetType()))
            return (IConfigurable)pluginInstance;

        return Activator.CreateInstance(configurableType) as IConfigurable;
    }

    /// <summary>Builds a pluginId -> (field Key -> schema DefaultValue) map from every loaded plugin's
    /// IConfigurable.GetConfigSchema(), so PluginSettingsService.GetSetting can fall back to a plugin's
    /// own declared default when nothing has been persisted yet, instead of every call site needing to
    /// duplicate that default in code. Group fields' SubFields are flattened in (they persist as
    /// independent top-level keys -- see PluginConfigFieldViewModel.Commit); Array/Object fields' own
    /// SubFields describe their single stored value's shape and are left nested under that field's Key.</summary>
    public static Dictionary<string, Dictionary<string, object?>> BuildSchemaDefaultsMap(PluginManager manager)
    {
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
            return result;

        // Only actual plugin entry assemblies (Lertaro.Plugins.*.dll) are processed.
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.StartsWith(pluginsDir, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(a.Location).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase));

        foreach (var assembly in loadedAssemblies)
        {
            try
            {
                var dllName = Path.GetFileName(assembly.Location);
                if (dllName.Equals("Lertaro.PluginSdk.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                var pluginType = assembly.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                var pluginInstance = pluginType != null ? manager.Plugins.FirstOrDefault(p => p.GetType() == pluginType) : null;

                var configurableInstance = ResolveConfigurable(assembly, pluginInstance);
                var schema = configurableInstance?.GetConfigSchema();
                if (schema?.Fields == null)
                    continue;

                var pluginId = Path.GetFileNameWithoutExtension(dllName);
                var fieldDefaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                FlattenFieldDefaults(schema.Fields, fieldDefaults);
                result[pluginId] = fieldDefaults;
            }
            catch { }
        }

        return result;
    }

    private static void FlattenFieldDefaults(IEnumerable<PluginConfigField> fields, Dictionary<string, object?> target)
    {
        foreach (var field in fields)
        {
            if (field.FieldType == ConfigFieldType.Group && field.SubFields != null)
            {
                FlattenFieldDefaults(field.SubFields, target);
            }
            else if (!string.IsNullOrEmpty(field.Key))
            {
                target[field.Key] = field.DefaultValue;
            }
        }
    }

    // internal (not private): reused by PluginManager.QuickNavigationProviders to build the same id
    // shape for its own ordering-by-persisted-list lookup, so the two never drift into different formats.
    internal static string MakeId(string dllName, PluginComponentType type, string name) => $"{dllName}::{type}::{name}";
}
