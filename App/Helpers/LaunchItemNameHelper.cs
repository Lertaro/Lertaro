using System.IO;

namespace Lertaro.App.Helpers;

internal static class LaunchItemNameHelper
{
    private static readonly HashSet<string> HiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".bat", ".cmd", ".exe"
    };

    public static string GetAutomaticName(string path)
        => HideKnownExtension(FavoritePathResolver.GetDisplayName(path));

    public static string HideKnownExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return HiddenExtensions.Contains(extension) ? Path.GetFileNameWithoutExtension(name) : name;
    }
}
