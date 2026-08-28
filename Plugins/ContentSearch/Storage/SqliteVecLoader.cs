using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Probes and initializes the native sqlite-vec extension (vec0.dll) from the system models directory when available.
/// </summary>
public static class SqliteVecLoader
{
    private static string? _cachedVecPath;
    private static bool _probeAttempted;

    public static bool TryEnableSqliteVec(SqliteConnection connection)
    {
        var vecPath = GetVecExtensionPath();
        if (string.IsNullOrEmpty(vecPath) || !File.Exists(vecPath))
            return false;

        try
        {
            connection.LoadExtension(vecPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetVecExtensionPath()
    {
        if (_probeAttempted)
            return _cachedVecPath;

        _probeAttempted = true;
        try
        {
            _cachedVecPath = ModelLocator.FindVecExtensionPath();
            return _cachedVecPath;
        }
        catch
        {
            // Extension probe failure is non-fatal; FTS5 lexical search remains fully operational.
        }

        return null;
    }

    internal static void ResetProbeForTesting()
    {
        _probeAttempted = false;
        _cachedVecPath = null;
    }
}
