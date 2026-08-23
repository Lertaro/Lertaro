using System.IO;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Discovers and provisions runtime interpreters for external Flow plugins (Python, Node.js).
/// Strictly isolates runtimes into SharedDataDirectory\FlowData\PythonEmbeded-{arch} and NodeEmbeded-{arch}
/// for machine-wide multi-user sharing.
/// </summary>
public static class FlowEnvironmentLocator
{
    private static string? _cachedPythonPath;
    private static string? _cachedNodePath;

    public static string? FindPythonExecutable()
    {
        var sharedDir = GetEmbeddedPythonDirectory();
        if (_cachedPythonPath != null && File.Exists(_cachedPythonPath))
        {
            FlowPythonDownloader.EnsureSiteCustomizeInstalled(sharedDir);
            return _cachedPythonPath;
        }

        var exe = FlowPythonDownloader.FindPythonInDir(sharedDir);
        if (exe != null)
        {
            FlowPythonDownloader.EnsureSiteCustomizeInstalled(sharedDir);
            _cachedPythonPath = exe;
            return _cachedPythonPath;
        }

        _cachedPythonPath = null;
        return null;
    }

    public static async Task<string?> EnsurePythonExecutableAsync()
    {
        var existing = FindPythonExecutable();
        if (existing != null)
            return existing;

        var targetDir = GetEmbeddedPythonDirectory();
        var downloaded = await FlowPythonDownloader.DownloadAndSetupEmbeddedPythonAsync(targetDir).ConfigureAwait(false);
        if (downloaded != null)
        {
            _cachedPythonPath = downloaded;
            return _cachedPythonPath;
        }

        return null;
    }

    public static string? FindNodeExecutable()
    {
        if (_cachedNodePath != null && File.Exists(_cachedNodePath))
            return _cachedNodePath;

        var sharedDir = GetEmbeddedNodeDirectory();
        var exe = FlowNodeDownloader.FindNodeInDir(sharedDir);
        if (exe != null)
        {
            _cachedNodePath = exe;
            return _cachedNodePath;
        }

        _cachedNodePath = null;
        return null;
    }

    public static async Task<string?> EnsureNodeExecutableAsync()
    {
        var existing = FindNodeExecutable();
        if (existing != null)
            return existing;

        var targetDir = GetEmbeddedNodeDirectory();
        var downloaded = await FlowNodeDownloader.DownloadAndSetupEmbeddedNodeAsync(targetDir).ConfigureAwait(false);
        if (downloaded != null)
        {
            _cachedNodePath = downloaded;
            return _cachedNodePath;
        }

        return null;
    }

    public static string GetEmbeddedPythonDirectory() => Path.Combine(GetSharedDataRoot(), "FlowData", $"PythonEmbeded-{GetArchSuffix()}");

    public static string GetEmbeddedNodeDirectory() => Path.Combine(GetSharedDataRoot(), "FlowData", $"NodeEmbeded-{GetArchSuffix()}");

    private static string GetSharedDataRoot() => PluginSdk.Services.UserDataService.GetSharedDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lertaro");

    private static string GetArchSuffix() => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "arm64"
        : "x64";
}
