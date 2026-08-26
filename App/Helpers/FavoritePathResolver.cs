using System.IO;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Helpers;

// Central resolution for favorite target paths: raw user input is kept everywhere for display and
// persistence, while backend navigation/search code resolves it through this helper. Environment
// variables are expanded first; shell virtual paths ("shell:..." / "::...") are then resolved via the
// ShellPathHelper (or an injected resolver in tests, since the real one is COM-backed).
public static class FavoritePathResolver
{
    public static string Expand(string? rawPath)
        => string.IsNullOrWhiteSpace(rawPath)
            ? (rawPath ?? string.Empty)
            : Environment.ExpandEnvironmentVariables(rawPath.Trim());

    public static bool IsVirtualPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var trimmed = path.Trim();
        return trimmed.StartsWith("::", StringComparison.Ordinal)
            || trimmed.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    }

    public static string Resolve(string? rawPath, Func<string, string>? virtualPathResolver = null)
    {
        var expanded = Expand(rawPath);
        return (virtualPathResolver ?? ShellPathHelper.TryResolveVirtualPath)(expanded);
    }

    public static string NormalizeForComparison(string? rawPath)
    {
        var expanded = Expand(rawPath);
        if (IsVirtualPath(expanded) || FavoriteUrlHelper.IsWebUrl(expanded))
            return expanded;

        try
        {
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return expanded;
        }
    }
}
