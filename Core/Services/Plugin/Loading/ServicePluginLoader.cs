using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.Services.Plugin.Loading;

public static class ServicePluginLoader
{
    public static void LoadForService() => LoadPlugins(loadHookPlugins: false);

    public static void LoadForHook() => LoadPlugins(loadHookPlugins: true);

    private static void LoadPlugins(bool loadHookPlugins)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var pluginsDir = Path.Combine(baseDir, "Plugins");

            Logger.Log($"[ServicePluginLoader] Scanning plugins in: {pluginsDir}");

            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                return;
            }

            var translationProviders = new List<ITranslationProvider>();
            var aliasProviders = new List<IAliasProvider>();

            // Sorted, not raw enumeration order: Directory.GetFiles doesn't guarantee an order, and
            // AliasProviderRegistry assigns each provider's numeric id by registration order -- an
            // unstable order here would reassign different ids to the same providers across restarts,
            // which would misattribute already-baked alias data tagged with the OLD ids (see
            // AliasProviderRegistry.ComputeProvidersFingerprint's own recompaction trigger, which
            // catches a genuine provider-set change but not pure reordering of an unchanged set).
            // Recursive: a plugin with its own dependency DLLs can sit in its own subdirectory (they
            // colocate with Assembly.LoadFrom's own implicit same-directory probing for dependency
            // resolution) instead of every DLL needing to live flat in Plugins/ directly.
            var dllFiles = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var dllFile in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dllFile);
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsInterface || type.IsAbstract)
                            continue;

                        var isAliasProvider = typeof(IAliasProvider).IsAssignableFrom(type);
                        if (!loadHookPlugins && !isAliasProvider)
                            continue;

                        if (typeof(IAliasProvider).IsAssignableFrom(type))
                        {
                            var provider = (IAliasProvider)Activator.CreateInstance(type)!;
                            aliasProviders.Add(provider);
                        }

                        if ((loadHookPlugins || isAliasProvider) && typeof(ITranslationProvider).IsAssignableFrom(type))
                        {
                            var provider = (ITranslationProvider)Activator.CreateInstance(type)!;
                            translationProviders.Add(provider);
                            if (loadHookPlugins)
                                Logger.Log($"[ServicePluginLoader] Loaded translation provider: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IActivePathCollector).IsAssignableFrom(type))
                        {
                            var provider = (IActivePathCollector)Activator.CreateInstance(type)!;
                            ActivePathCollectorRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded active path collector: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IFileDialogAdapter).IsAssignableFrom(type))
                        {
                            var provider = (IFileDialogAdapter)Activator.CreateInstance(type)!;
                            FileDialogAdapterRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded file dialog adapter: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }

                        if (loadHookPlugins && typeof(IInlineSearchAdapter).IsAssignableFrom(type))
                        {
                            var provider = (IInlineSearchAdapter)Activator.CreateInstance(type)!;
                            InlineSearchAdapterRegistry.Register(provider);
                            Logger.Log($"[ServicePluginLoader] Loaded inline search adapter: '{type.Name}' from {Path.GetFileName(dllFile)}");
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    // Not a .NET assembly at all -- expected for a plugin's own bundled native
                    // dependency (e.g. a SQLite provider's e_sqlite3.dll) now that the scan is
                    // recursive into each plugin's own subdirectory. Not a failure, so not worth an
                    // Error-level log line.
                    Logger.Log($"[ServicePluginLoader] Skipped non-.NET file: {Path.GetFileName(dllFile)}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ServicePluginLoader] Failed to load plugin assembly {Path.GetFileName(dllFile)}: {ex.Message}", LogLevel.Error);
                }
            }

            // Initialize TranslationService LookupFunc in the service process using the loaded translation providers
            var cultureName = System.Globalization.CultureInfo.CurrentUICulture.Name;
            ServicePluginServiceWiring.WireTranslations(translationProviders, cultureName);
            ServicePluginServiceWiring.WirePluginSettings();

            if (loadHookPlugins)
            {
                PluginComponentEnablement.WireFilterFuncs();
            }

            // Now register alias providers (this will trigger provider.Name evaluation)
            foreach (var provider in aliasProviders)
            {
                AliasProviderRegistry.Register(provider);
                Logger.Log($"[ServicePluginLoader] Loaded alias provider: '{provider.GetType().Name}'");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ServicePluginLoader] Error while loading plugins: {ex.Message}", LogLevel.Error);
        }
    }
}
