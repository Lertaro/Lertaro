using System.IO;

namespace Lertaro.Plugins.Files.Automation;

/// <summary>
/// Validates a path string by shape rather than round-tripping through the filesystem.
/// Directory.Exists can spuriously return false for a perfectly valid path when this runs in the elevated
/// hook/service process and the path is a drive mapped via "net use" in a normal (non-elevated) logon
/// session: Windows scopes those mappings to the session/elevation context that created them, so an
/// elevated process can't see them even though the (non-elevated) host app displaying the path sees it
/// just fine. Trusting the string's shape avoids that false negative for network drives.
/// </summary>
internal static class PathValidation
{
    public static bool LooksLikeRootedPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && (path.Contains(":\\") || path.StartsWith("\\\\"));
}
