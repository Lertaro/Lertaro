using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Helper for instantiating Flow.Launcher plugin runtime instances across languages.
/// Split out from FlowPluginHost to stay strictly under the line limit.
/// </summary>
public static class FlowPluginLoaderHelper
{
    public static async Task<IAsyncPlugin?> CreatePluginInstanceAsync(
        PluginMetadata metadata,
        string pluginDir,
        ConcurrentDictionary<string, FlowAssemblyLoader> loaders,
        ConcurrentDictionary<string, (PluginMetadata Metadata, string Reason)> failedPlugins)
    {
        if (AllowedLanguage.IsDotNet(metadata.Language) && !string.IsNullOrEmpty(metadata.ExecuteFilePath) && File.Exists(metadata.ExecuteFilePath))
        {
            var loader = new FlowAssemblyLoader(pluginDir, metadata.ExecuteFilePath);
            loaders[metadata.ID] = loader;
            var assembly = loader.LoadAssemblyFromBytes(metadata.ExecuteFilePath);
            var instance = CreateDotNetPluginInstance(assembly);
            if (instance == null)
            {
                loaders.TryRemove(metadata.ID, out _);
                try { loader.Unload(); } catch { }
                failedPlugins[metadata.ID] = (metadata, "No implementation of IPlugin or IAsyncPlugin found.");
                return null;
            }
            return instance;
        }

        if (AllowedLanguage.IsPython(metadata.Language))
        {
            var pythonPath = await FlowEnvironmentLocator.EnsurePythonExecutableAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(pythonPath))
            {
                failedPlugins[metadata.ID] = (metadata, "Python runtime not found in PythonEmbeded, or download failed.");
                return null;
            }
            FlowPipManager.EnsurePipAndRequirementsBackground(pythonPath, pluginDir);
            var runner = new FlowProcessRunner(metadata, pythonPath, metadata.ExecuteFilePath);
            return new FlowJsonRpcPlugin(runner, metadata);
        }

        if (AllowedLanguage.IsNodeJs(metadata.Language))
        {
            var nodePath = await FlowEnvironmentLocator.EnsureNodeExecutableAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(nodePath) || !File.Exists(nodePath))
            {
                failedPlugins[metadata.ID] = (metadata, "Node.js runtime not found in NodeEmbeded, and download failed.");
                return null;
            }
            FlowNpmManager.EnsureNpmAndPackagesBackground(nodePath, pluginDir);
            var runner = new FlowProcessRunner(metadata, nodePath, metadata.ExecuteFilePath);
            return new FlowJsonRpcPlugin(runner, metadata);
        }

        if (AllowedLanguage.IsExecutable(metadata.Language))
        {
            if (string.IsNullOrEmpty(metadata.ExecuteFilePath) || !File.Exists(metadata.ExecuteFilePath))
            {
                failedPlugins[metadata.ID] = (metadata, $"Executable file not found: {metadata.ExecuteFilePath}");
                return null;
            }
            var runner = new FlowProcessRunner(metadata, metadata.ExecuteFilePath);
            return new FlowJsonRpcPlugin(runner, metadata);
        }

        return null;
    }

    public static IAsyncPlugin? CreateDotNetPluginInstance(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsInterface || type.IsAbstract)
                continue;

            if (typeof(IAsyncPlugin).IsAssignableFrom(type))
                return Activator.CreateInstance(type) as IAsyncPlugin;

            if (typeof(IPlugin).IsAssignableFrom(type))
            {
                if (Activator.CreateInstance(type) is IPlugin syncPlugin)
                    return new FlowSyncPluginAdapter(syncPlugin);
            }
        }
        return null;
    }
}
