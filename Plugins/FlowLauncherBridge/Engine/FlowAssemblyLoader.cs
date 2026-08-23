using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Isolated assembly load context for third-party Flow.Launcher .NET plugins.
/// Resolves local dependency assemblies located within the plugin's folder.
/// </summary>
public class FlowAssemblyLoader : AssemblyLoadContext
{
    private readonly string _pluginDirectory;

    public FlowAssemblyLoader(string pluginDirectory) : base(isCollectible: true) => _pluginDirectory = pluginDirectory;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, "Flow.Launcher.Plugin", StringComparison.OrdinalIgnoreCase))
        {
            // Always bind to host's loaded Flow.Launcher.Plugin assembly
            return typeof(Flow.Launcher.Plugin.IPlugin).Assembly;
        }

        var dllPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(dllPath))
        {
            return LoadFromAssemblyPath(dllPath);
        }

        return null;
    }
}
