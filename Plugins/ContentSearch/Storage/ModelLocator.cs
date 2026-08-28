using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Locates machine-wide model assets and vector extensions stored in the system shared data directory.
/// </summary>
public static class ModelLocator
{
    private static string? _cachedModelsDir;

    /// <summary>
    /// Gets the machine-wide directory for models (e.g. %ProgramData%\Lertaro\Models\ContentSearch or portable Data\Machine\Models\ContentSearch).
    /// </summary>
    public static string GetModelsDirectory()
    {
        if (_cachedModelsDir != null)
            return _cachedModelsDir;

        var sharedDir = UserDataService.GetSharedDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lertaro");

        _cachedModelsDir = Path.Combine(sharedDir, "Models", "ContentSearch");
        return _cachedModelsDir;
    }

    /// <summary>
    /// Resolves the absolute path to a named model file located in the models directory.
    /// </summary>
    public static string? FindModelFile(string fileName)
    {
        var dir = GetModelsDirectory();
        var fullPath = Path.Combine(dir, fileName);
        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Probes for the native sqlite-vec extension (vec0.dll) in the machine-wide models directory.
    /// </summary>
    public static string? FindVecExtensionPath()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };

        if (arch == null)
            return null;

        var modelsDir = GetModelsDirectory();
        var candidates = new[]
        {
            Path.Combine(modelsDir, arch, "vec0.dll"),
            Path.Combine(modelsDir, "vec0.dll"),
            Path.Combine(modelsDir, "runtimes", arch, "native", "vec0.dll")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    internal static void ResetCacheForTesting() => _cachedModelsDir = null;
}
