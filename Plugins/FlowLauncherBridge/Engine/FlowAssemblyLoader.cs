using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Isolated assembly load context for third-party Flow.Launcher .NET plugins.
/// Loads assemblies via memory streams to avoid locking DLL files on Windows, allowing instant uninstallation and updates.
/// </summary>
public class FlowAssemblyLoader : AssemblyLoadContext
{
    private readonly string _pluginDirectory;
    private readonly AssemblyDependencyResolver? _resolver;

    public FlowAssemblyLoader(string pluginDirectory, string? mainDllPath = null) : base(isCollectible: true)
    {
        _pluginDirectory = pluginDirectory;
        var targetPath = !string.IsNullOrEmpty(mainDllPath) ? mainDllPath : Directory.GetFiles(pluginDirectory, "*.dll").FirstOrDefault();
        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
        {
            _resolver = new AssemblyDependencyResolver(targetPath);
        }
    }

    public Assembly LoadAssemblyFromBytes(string dllPath) => LoadFromAssemblyPath(dllPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, "Flow.Launcher.Plugin", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(Flow.Launcher.Plugin.IPlugin).Assembly;
        }

        var resolvedPath = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        var dllPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(dllPath))
        {
            return LoadFromAssemblyPath(dllPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolvedPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
        {
            return LoadUnmanagedDllFromPath(resolvedPath);
        }

        var fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? unmanagedDllName : $"{unmanagedDllName}.dll";
        var dllPath = Path.Combine(_pluginDirectory, fileName);
        if (File.Exists(dllPath))
        {
            return LoadUnmanagedDllFromPath(dllPath);
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
