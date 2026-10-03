using System.IO;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Discovers and provisions runtime interpreters for external Flow plugins (Python, Node.js).
/// Strictly isolates runtimes into UserDataDirectory\FlowData\PythonEmbeded-{arch} and NodeEmbeded-{arch}.
/// </summary>
/// <remarks>
/// Per user, not machine-wide: a runtime folder every user can add files to lets one user plant a DLL or
/// sitecustomize.py that runs inside another user's App, and the machine data directory is locked to the
/// service besides. ponytail: each user downloads their own copy (tens of MB); a shared copy would need the
/// service to do the download.
/// </remarks>
public static class FlowEnvironmentLocator
{
    private static string? _cachedPythonPath;
    private static string? _cachedNodePath;

    internal static void ResetCache()
    {
        _cachedPythonPath = null;
        _cachedNodePath = null;
    }

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

    public static string GetEmbeddedPythonDirectory() => Path.Combine(FlowPipManager.GetFlowDataDirectory(), $"PythonEmbeded-{GetArchSuffix()}");

    public static string GetEmbeddedNodeDirectory() => Path.Combine(FlowPipManager.GetFlowDataDirectory(), $"NodeEmbeded-{GetArchSuffix()}");

    private static string GetArchSuffix() => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "arm64"
        : "x64";
}
