using System.IO;

namespace Lertaro.Plugins.OneCommander.Automation;

/// <summary>
/// Validates a physical directory before it becomes an inline-search scope or cascading-menu entry.
/// </summary>
internal static class PathValidation
{
    public static bool IsAccessibleDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (IsWslPath(path) || Directory.Exists(path));

    private static bool IsWslPath(string path) =>
        path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
}
