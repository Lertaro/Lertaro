using System.IO;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Discovers and provisions runtime interpreters for external Flow plugins (Python, Node.js).
/// Strictly isolates Python to UserDataDirectory\PythonEmbeded and resolves Node.js via system PATH.
/// </summary>
public static class FlowEnvironmentLocator
{
    private static string? _cachedPythonPath;
    private static string? _cachedNodePath;
    private static bool _pythonSearched;
    private static bool _nodeSearched;

    public static string? FindPythonExecutable()
    {
        if (_pythonSearched && _cachedPythonPath != null && File.Exists(_cachedPythonPath))
            return _cachedPythonPath;

        var embedDir = GetEmbeddedPythonDirectory();
        var exe = FlowPythonDownloader.FindPythonInDir(embedDir);
        if (exe != null)
        {
            _cachedPythonPath = exe;
            _pythonSearched = true;
            return _cachedPythonPath;
        }

        _cachedPythonPath = null;
        _pythonSearched = true;
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
            _pythonSearched = true;
            return _cachedPythonPath;
        }

        return null;
    }

    public static string? FindNodeExecutable()
    {
        if (_nodeSearched && _cachedNodePath != null && File.Exists(_cachedNodePath))
            return _cachedNodePath;

        _cachedNodePath = ProbePath("node.exe");
        _nodeSearched = true;
        return _cachedNodePath;
    }

    public static string GetEmbeddedPythonDirectory()
    {
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "PythonEmbeded");
    }

    private static string? ProbePath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(dir.Trim(), binaryName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }
}
