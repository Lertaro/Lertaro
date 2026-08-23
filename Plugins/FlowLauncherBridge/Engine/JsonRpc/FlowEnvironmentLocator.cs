using System.IO;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Discovers and provisions runtime interpreters for external Flow plugins (Python, Node.js).
/// Strictly and exclusively isolates runtimes into UserDataDirectory\FlowData\PythonEmbeded-{arch} and NodeEmbeded-{arch}.
/// </summary>
public static class FlowEnvironmentLocator
{
    private static string? _cachedPythonPath;
    private static string? _cachedNodePath;

    public static string? FindPythonExecutable()
    {
        if (_cachedPythonPath != null && File.Exists(_cachedPythonPath))
            return _cachedPythonPath;

        var embedDir = GetEmbeddedPythonDirectory();
        var exe = FlowPythonDownloader.FindPythonInDir(embedDir);
        if (exe != null)
        {
            FlowPythonDownloader.EnsureSiteCustomizeInstalled(embedDir);
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

        var embedDir = GetEmbeddedNodeDirectory();
        var exe = FlowNodeDownloader.FindNodeInDir(embedDir);
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

    public static string GetEmbeddedPythonDirectory() => Path.Combine(GetUserDataRoot(), "FlowData", $"PythonEmbeded-{GetArchSuffix()}");

    public static string GetEmbeddedNodeDirectory() => Path.Combine(GetUserDataRoot(), "FlowData", $"NodeEmbeded-{GetArchSuffix()}");

    private static string GetUserDataRoot() => PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");

    private static string GetArchSuffix() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };
}
