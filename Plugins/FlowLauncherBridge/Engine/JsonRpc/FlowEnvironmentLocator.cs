using System.IO;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Discovers runtime interpreters for external Flow plugins (Python, Node.js).
/// </summary>
public static class FlowEnvironmentLocator
{
    private static string? _cachedPythonPath;
    private static string? _cachedNodePath;
    private static bool _pythonSearched;
    private static bool _nodeSearched;

    public static string? FindPythonExecutable()
    {
        if (_pythonSearched)
            return _cachedPythonPath;

        _cachedPythonPath = ProbeExecutable(["python.exe", "python3.exe", "py.exe"], GetPythonProbingDirectories());
        _pythonSearched = true;
        return _cachedPythonPath;
    }

    public static string? FindNodeExecutable()
    {
        if (_nodeSearched)
            return _cachedNodePath;

        _cachedNodePath = ProbeExecutable(["node.exe"], GetNodeProbingDirectories());
        _nodeSearched = true;
        return _cachedNodePath;
    }

    private static string? ProbeExecutable(string[] binaryNames, IEnumerable<string> searchDirs)
    {
        // 1. Search PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var binary in binaryNames)
                {
                    var fullPath = Path.Combine(dir.Trim(), binary);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }
        }

        // 2. Search well-known directory locations
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var binary in binaryNames)
            {
                var fullPath = Path.Combine(dir, binary);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPythonProbingDirectories()
    {
        var dirs = new List<string>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var pythonBase = Path.Combine(localAppData, "Programs", "Python");
        if (Directory.Exists(pythonBase))
        {
            try { dirs.AddRange(Directory.GetDirectories(pythonBase)); } catch { }
        }

        if (Directory.Exists(progFiles))
        {
            try
            {
                dirs.AddRange(Directory.GetDirectories(progFiles, "Python*"));
            }
            catch { }
        }

        return dirs;
    }

    private static IEnumerable<string> GetNodeProbingDirectories()
    {
        var dirs = new List<string>();
        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var nodeDir = Path.Combine(progFiles, "nodejs");
        if (Directory.Exists(nodeDir))
            dirs.Add(nodeDir);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fnmDir = Path.Combine(localAppData, "fnm_multishells");
        if (Directory.Exists(fnmDir))
            dirs.Add(fnmDir);

        var nvmDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvm");
        if (Directory.Exists(nvmDir))
            dirs.Add(nvmDir);

        return dirs;
    }
}
