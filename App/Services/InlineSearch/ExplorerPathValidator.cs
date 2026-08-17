using System.IO;
using Lertaro.Core;

namespace Lertaro.App.Services;

internal static class ExplorerPathValidator
{
    public static bool IsUsableDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        (WslPath.IsPath(path) || Directory.Exists(path));

    public static string NormalizeDirectory(string path) => IsUsableDirectory(path) ? path : string.Empty;

    public static IReadOnlyList<string> FilterReportedDirectories(IEnumerable<string> paths) =>
        paths.Where(IsUsableDirectory)
            .Select(Path.TrimEndingDirectorySeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
