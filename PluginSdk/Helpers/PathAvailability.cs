using System.IO;

namespace Lertaro.PluginSdk.Helpers;

public static class PathAvailability
{
    public static bool IsAvailable(string? path)
    {
        var expanded = UserPathResolver.Expand(path);
        if (UserPathResolver.IsVirtualPath(expanded))
        {
            return ShellVirtualPathValidator.Exists(expanded);
        }

        return File.Exists(expanded) || Directory.Exists(expanded);
    }

    public static bool IsFolderAvailable(string? path)
    {
        var expanded = UserPathResolver.Expand(path);
        if (UserPathResolver.IsVirtualPath(expanded))
        {
            return ShellVirtualPathValidator.Exists(expanded, requireFolder: true);
        }

        return Directory.Exists(expanded);
    }
}
