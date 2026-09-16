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

    // The trust model for Plugins/, stated once because everything below depends on it: the directory
    // ships with the installation, and an assembly placed in it is loaded into this process and runs with
    // its privileges. There is no sandbox and no signature check here, so the ONLY thing separating a
    // plugin from a bundled dependency is the naming rule in IsPluginEntryAssembly -- enforced, not
    // assumed. A caller who can write into Plugins/ can already run code as this process, so the rule is
    // about not widening that by accident (probing third-party code for our own contracts, or letting a
    // stray DLL inject a provider), not about containing a hostile plugin.
    //
    // Mirrors App/Services/PluginManagerCore/PluginLoader.Load's identical rule; the two loaders must
    // agree, because AliasProviderRegistry assigns each provider's numeric id by registration order --
    // a provider registered in one process but not the other would shift every later id and misattribute
    // already-baked alias data.
    internal static bool IsPluginEntryAssembly(string dllPath)
        => Path.GetFileName(dllPath).StartsWith("Lertaro.Plugins.", StringComparison.OrdinalIgnoreCase);

    // The rule plus the ordering, as one pure step, so both halves of the decision are pinned by a test
    // rather than only exercised through a real Plugins/ tree. Ordering is part of the trust decision
    // rather than a nicety: AliasProviderRegistry assigns ids by registration order, so an unstable order
    // here would reassign ids between restarts.
    internal static string[] SelectPluginEntryAssemblies(IEnumerable<string> dllPaths)
        => [.. dllPaths.Where(IsPluginEntryAssembly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

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
            var candidates = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories);
            var dllFiles = SelectPluginEntryAssemblies(candidates);

            // Recorded, not silent: which files were treated as plugin entries and which were dismissed
            // as dependencies is the whole of this loader's trust decision, and the only way to audit it
            // after the fact is to have written it down. Loading a dependency here would probe
            // third-party code (Microsoft.Data.Sqlite, UglyToad.PdfPig, Flow.Launcher.Plugin, a native
            // e_sqlite3, ...) for our own contracts for no gain -- its types are reachable anyway once
            // the plugin entry assembly that needs it loads.
            Logger.Log($"[ServicePluginLoader] {dllFiles.Length} plugin entry assembly/assemblies of {candidates.Length} DLL(s) under {pluginsDir}; the rest are treated as bundled dependencies.", LogLevel.Debug);

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
                    // Not a .NET assembly at all. The naming filter above means a plugin's bundled native
                    // dependency (e.g. a SQLite provider's e_sqlite3.dll) never reaches this loop, so what
                    // lands here is a file that IS named like a plugin entry (Lertaro.Plugins.*.dll) without
                    // being a managed assembly -- worth knowing about, but not a load failure either.
                    Logger.Log($"[ServicePluginLoader] Skipped non-.NET file: {Path.GetFileName(dllFile)}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ServicePluginLoader] Failed to load plugin assembly {Path.GetFileName(dllFile)}: {ex.Message}", LogLevel.Error);
                }
            }

            // Translation providers may read plugin settings while loading; bind the reader before
            // asking them for translations so configuration-gated startup work observes persisted values.
            ServicePluginServiceWiring.WirePluginSettings();

            // Initialize TranslationService LookupFunc in the service process using the loaded translation providers
            var cultureName = System.Globalization.CultureInfo.CurrentUICulture.Name;
            ServicePluginServiceWiring.WireTranslations(translationProviders, cultureName);

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
