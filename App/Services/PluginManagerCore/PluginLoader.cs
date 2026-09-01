using System.IO;
using System.Reflection;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.Services.PluginManagerCore;

/// <summary>
/// Scans the <c>Plugins/</c> directory for DLL assemblies and registers every
/// recognised <see cref="PluginSdk.Abstractions.Plugins.IPlugin"/>, <see cref="IAliasProvider"/>,
/// <see cref="PluginSdk.Abstractions.Plugins.IInstantResultProvider"/>, <see cref="PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider"/>,
/// <see cref="PluginSdk.Abstractions.Plugins.ISidebarFilterProvider"/>,
/// <see cref="PluginSdk.Abstractions.Plugins.IResultColumnProvider"/> and <see cref="PluginSdk.Abstractions.Plugins.ITranslationProvider"/>.
/// </summary>
internal static class PluginLoader
{
    /// <summary>
    /// Discovers and loads all plugin DLLs, delegating registration back to
    /// <paramref name="registry"/> via the supplied callbacks.
    /// </summary>
    internal static void Load(PluginRegistry registry)
    {
        try
        {
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(pluginsDir))
                Directory.CreateDirectory(pluginsDir);

            // Recursive: a plugin with its own dependency DLLs can sit in its own subdirectory (they
            // colocate with Assembly.LoadFrom's own implicit same-directory probing for dependency
            // resolution) instead of every DLL needing to live flat in Plugins/ directly.
            // Only actual plugin entry assemblies (Lertaro.Plugins.*.dll) are scanned directly.
            // Dependency DLLs are resolved implicitly from the plugin's own directory when its main
            // assembly loads; loading them here as if they were plugins can fail with
            // "Assembly with same name is already loaded" when two plugins bundle the same package
            // (e.g. Microsoft.Data.Sqlite in both BrowserData and ContentSearch).
            foreach (var dllFile in Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(dllFile);
                if (!assemblyName.StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase))
                    continue;
                TryLoadAssembly(dllFile, registry);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Error while loading plugins: {ex.Message}", LogLevel.Error);
        }

        // TranslationManager is reloaded explicitly in App.xaml.cs after all plugins are loaded,
        // to avoid a circular Lazy<T> initialization between PluginManager and TranslationManager.
    }

    private static void TryLoadAssembly(string dllFile, PluginRegistry registry)
    {
        var fileName = Path.GetFileName(dllFile);
        try
        {
            var assembly = Assembly.LoadFrom(dllFile);
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsInterface || type.IsAbstract)
                    continue;

                if (typeof(PluginSdk.Abstractions.Plugins.IPlugin).IsAssignableFrom(type))
                {
                    var plugin = (PluginSdk.Abstractions.Plugins.IPlugin)Activator.CreateInstance(type)!;
                    registry.RegisterPlugin(plugin);
                    var pluginVer = assembly.GetName().Version?.ToString(3) ?? "1.0.0";
                    Logger.Log($"[PluginManager] Loaded plugin: '{type.Name}' (v{pluginVer}) from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IAliasProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IAliasProvider)Activator.CreateInstance(type)!;
                    AliasProviderRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded alias provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IInstantResultProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IInstantResultProvider)Activator.CreateInstance(type)!;
                    registry.AddInstantResultProvider(provider);
                    Logger.Log($"[PluginManager] Loaded instant result provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider)Activator.CreateInstance(type)!;
                    registry.AddFullSearchFileResultProvider(provider);
                    Logger.Log($"[PluginManager] Loaded full search file result provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ISearchableItemProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ISearchableItemProvider)Activator.CreateInstance(type)!;
                    registry.AddSearchableItemProvider(provider);
                    Logger.Log($"[PluginManager] Loaded searchable item provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ISidebarFilterProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ISidebarFilterProvider)Activator.CreateInstance(type)!;
                    registry.AddSidebarFilterProvider(provider);
                    Logger.Log($"[PluginManager] Loaded sidebar filter provider from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IResultColumnProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IResultColumnProvider)Activator.CreateInstance(type)!;
                    registry.AddResultColumnProvider(provider);
                    Logger.Log($"[PluginManager] Loaded result column provider from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.ITranslationProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.ITranslationProvider)Activator.CreateInstance(type)!;
                    registry.AddTranslationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded translation provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IThemeProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IThemeProvider)Activator.CreateInstance(type)!;
                    registry.AddThemeProvider(provider);
                    Logger.Log($"[PluginManager] Loaded theme provider: '{type.Name}' from {fileName}");
                }

                if (typeof(IActivePathCollector).IsAssignableFrom(type))
                {
                    var provider = (IActivePathCollector)Activator.CreateInstance(type)!;
                    registry.AddActivePathCollector(provider);
                    Logger.Log($"[PluginManager] Loaded active path collector: '{type.Name}' from {fileName}");
                }

                if (typeof(IFilePreviewProvider).IsAssignableFrom(type))
                {
                    var provider = (IFilePreviewProvider)Activator.CreateInstance(type)!;
                    registry.AddFilePreviewProvider(provider);
                    Logger.Log($"[PluginManager] Loaded file preview provider: '{type.Name}' from {fileName}");
                }

                if (typeof(IFileDialogAdapter).IsAssignableFrom(type))
                {
                    var provider = (IFileDialogAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.Registries.FileDialogAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded file dialog adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(IInlineSearchAdapter).IsAssignableFrom(type))
                {
                    var provider = (IInlineSearchAdapter)Activator.CreateInstance(type)!;
                    PluginSdk.Registries.InlineSearchAdapterRegistry.Register(provider);
                    Logger.Log($"[PluginManager] Loaded inline search adapter: '{type.Name}' from {fileName}");
                }

                if (typeof(IQuickNavigationProvider).IsAssignableFrom(type))
                {
                    var provider = (IQuickNavigationProvider)Activator.CreateInstance(type)!;
                    registry.AddQuickNavigationProvider(provider);
                    Logger.Log($"[PluginManager] Loaded quick navigation provider: '{type.Name}' from {fileName}");
                }

                if (typeof(IThumbnailProvider).IsAssignableFrom(type))
                {
                    var provider = (IThumbnailProvider)Activator.CreateInstance(type)!;
                    registry.AddThumbnailProvider(provider);
                    Logger.Log($"[PluginManager] Loaded thumbnail provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IQueryTokenProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IQueryTokenProvider)Activator.CreateInstance(type)!;
                    registry.AddQueryTokenProvider(provider);
                    Logger.Log($"[PluginManager] Loaded query token provider: '{type.Name}' from {fileName}");
                }

                if (typeof(PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider).IsAssignableFrom(type))
                {
                    var provider = (PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider)Activator.CreateInstance(type)!;
                    registry.AddQuickPanelTabProvider(provider);
                    Logger.Log($"[PluginManager] Loaded quick panel tab provider: '{type.Name}' from {fileName}");
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Not a .NET assembly at all -- expected for a plugin's own bundled native dependency
            // (e.g. a SQLite provider's e_sqlite3.dll) now that the scan is recursive into each
            // plugin's own subdirectory. Not a failure, so not worth an Error-level log line.
            Logger.Log($"[PluginManager] Skipped non-.NET file: {fileName}", LogLevel.Debug);
        }
        catch (FileLoadException ex) when (ex.Message.Contains("same name is already loaded", StringComparison.OrdinalIgnoreCase))
        {
            // Two plugins can legitimately bundle the same dependency (e.g. Microsoft.Data.Sqlite).
            // The second copy is not a plugin entry assembly, so this is expected -- keep it quiet.
            Logger.Log($"[PluginManager] Skipped duplicate dependency assembly: {fileName}", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Failed to load assembly {fileName}: {ex.Message}", LogLevel.Error);
        }
    }
}
