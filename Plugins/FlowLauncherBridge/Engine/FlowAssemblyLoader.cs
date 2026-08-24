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
    private readonly string _shadowDirectory;
    private readonly AssemblyDependencyResolver? _resolver;

    public string ShadowDirectory => _shadowDirectory;

    public FlowAssemblyLoader(string pluginDirectory, string? mainDllPath = null) : base(isCollectible: true)
    {
        _shadowDirectory = CreateShadowCopy(pluginDirectory);
        var mainDllName = !string.IsNullOrEmpty(mainDllPath) ? Path.GetFileName(mainDllPath) : Directory.GetFiles(_shadowDirectory, "*.dll").Select(Path.GetFileName).FirstOrDefault();
        var shadowMainDll = !string.IsNullOrEmpty(mainDllName) ? Path.Combine(_shadowDirectory, mainDllName) : null;
        if (!string.IsNullOrEmpty(shadowMainDll) && File.Exists(shadowMainDll))
        {
            _resolver = new AssemblyDependencyResolver(shadowMainDll);
        }
    }

    public Assembly LoadAssemblyFromBytes(string originalDllPath)
    {
        var dllName = Path.GetFileName(originalDllPath);
        var shadowDll = Path.Combine(_shadowDirectory, dllName);
        return LoadFromAssemblyPath(File.Exists(shadowDll) ? shadowDll : originalDllPath);
    }

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

        var dllPath = Path.Combine(_shadowDirectory, $"{assemblyName.Name}.dll");
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
        var dllPath = Path.Combine(_shadowDirectory, fileName);
        if (File.Exists(dllPath))
        {
            return LoadUnmanagedDllFromPath(dllPath);
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }

    private static string CreateShadowCopy(string sourceDir)
    {
        var shadowDir = Path.Combine(Path.GetTempPath(), "LertaroFlowShadow", Guid.NewGuid().ToString("N"));
        if (!Directory.Exists(sourceDir)) return shadowDir;

        try
        {
            Directory.CreateDirectory(shadowDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var dest = Path.Combine(shadowDir, relative);
                var destParent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destParent)) Directory.CreateDirectory(destParent);
                File.Copy(file, dest, true);
            }
        }
        catch { }

        return shadowDir;
    }
}
