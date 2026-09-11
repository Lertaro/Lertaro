using System.IO;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Helpers;

// Central resolution for favorite target paths: raw user input is kept everywhere for display and
// persistence, while backend navigation/search code resolves it through this helper. The two parsing
// steps it is built from -- "%VAR%" expansion and shell virtual paths ("shell:..." / "::...") -- live in
// UserPathResolver, which is the plugin-visible half of the same rule (plugins cannot reference this
// App-side class); what stays here are the favorites-specific policies layered on top of them.
public static class FavoritePathResolver
{
    public static string Expand(string? rawPath) => UserPathResolver.Expand(rawPath);

    public static bool IsVirtualPath(string? path) => UserPathResolver.IsVirtualPath(path);

    public static string Resolve(string? rawPath, Func<string, string>? virtualPathResolver = null)
        => UserPathResolver.Resolve(rawPath, virtualPathResolver);

    public static string GetDisplayName(string? rawPath)
    {
        var expanded = Expand(rawPath);
        if (IsVirtualPath(expanded))
        {
            var shellName = ShellPathHelper.GetVirtualFolderDisplayName(expanded, string.Empty);
            if (!string.IsNullOrWhiteSpace(shellName))
                return shellName;
        }

        return QuickPanelFolderSource.DefaultName(Resolve(expanded));
    }

    public static bool IsPathAvailable(
        string? rawPath,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? virtualPathExists = null)
    {
        var expanded = Expand(rawPath);
        if (string.IsNullOrWhiteSpace(expanded)) return false;
        if (FavoriteUrlHelper.IsWebUrl(expanded)) return true;

        if (IsVirtualPath(expanded))
        {
            return virtualPathExists is null
                ? ShellVirtualPathValidator.Exists(expanded)
                : virtualPathExists(expanded);
        }

        if (fileExists == null && directoryExists == null && expanded.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase))
            return ViewModels.Search.SearchReachabilityGate.IsPathReachable(expanded);

        return (fileExists ?? File.Exists)(expanded) || (directoryExists ?? Directory.Exists)(expanded);
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
