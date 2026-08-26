using System.IO;

namespace Lertaro.PluginSdk.Helpers;

public static class PathAvailability
{
    public static bool IsAvailable(string? path)
    {
        var expanded = Expand(path);
        if (IsVirtualPath(expanded))
        {
            return ShellVirtualPathValidator.Exists(expanded);
        }

        return File.Exists(expanded) || Directory.Exists(expanded);
    }

    public static bool IsFolderAvailable(string? path)
    {
        var expanded = Expand(path);
        if (IsVirtualPath(expanded))
        {
            return ShellVirtualPathValidator.Exists(expanded, requireFolder: true);
        }

        return Directory.Exists(expanded);
    }

    private static string Expand(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Environment.ExpandEnvironmentVariables(path.Trim());

    private static bool IsVirtualPath(string path) =>
        path.StartsWith("::", StringComparison.Ordinal)
        || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
}
