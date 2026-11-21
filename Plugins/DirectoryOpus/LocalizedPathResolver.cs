using System.IO;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>
/// Resolves localized display paths such as <c>C:\用户\testuser</c> to filesystem paths such as
/// <c>C:\Users\testuser</c>.
/// </summary>
/// <remarks>
/// Opus window titles contain localized names. Its XML element text contains the actual path and is
/// handled by <see cref="DopusRtPathQuery"/>; this resolver serves the window-title fallback.
///
/// Walk segments, accepting existing names directly and otherwise comparing child shell display names
/// through <see cref="ShellPathHelper.GetLocalizedFolderName"/>. Return the original path if a segment
/// cannot be resolved. Paths without localized segments need no enumeration.
///
/// ponytail: each localized segment requires a directory listing and SHGetFileInfo per child.
/// If this becomes a hot path, cache each directory's display names.
/// </remarks>
internal static class LocalizedPathResolver
{
    /// <summary>
    /// The real path <paramref name="reported"/> names, or <paramref name="reported"/> itself when it is
    /// already real, is not a rooted path, or cannot be resolved.
    /// </summary>
    /// <remarks>
    /// A segment that exists under its reported spelling always wins, so a real folder that happens to
    /// be named exactly like another folder's localized name is taken at face value: Opus reports what it
    /// is showing, and the file system's answer about that exact spelling is the more direct evidence.
    /// </remarks>
    public static string Resolve(string reported)
    {
        if (string.IsNullOrWhiteSpace(reported)) return reported;

        try
        {
            var root = Path.GetPathRoot(reported);
            // Not an absolute path ("Desktop", "::{20D04FE0-...}"): nothing to walk, and the caller's
            // existing special-folder/virtual-path handling is what should see it.
            if (string.IsNullOrEmpty(root)) return reported;

            var segments = reported[root.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

            var current = root;
            foreach (var segment in segments)
            {
                var direct = Path.Combine(current, segment);
                if (Directory.Exists(direct))
                {
                    current = direct;
                    continue;
                }

                var localized = FindByDisplayName(current, segment);
                // Unresolvable segment: the whole path is suspect, so report it unchanged rather than
                // handing back a guess stitched together from the segments that did resolve.
                if (localized == null) return reported;

                current = localized;
            }

            return current;
        }
        catch (Exception)
        {
            // Any I/O failure while probing (an unreadable or vanishing directory) means "cannot
            // translate", which the caller treats as "use what was reported".
            return reported;
        }
    }

    /// <summary>The child of <paramref name="parent"/> whose shell display name is <paramref name="localizedName"/>, or null.</summary>
    private static string? FindByDisplayName(string parent, string localizedName)
    {
        foreach (var child in Directory.EnumerateDirectories(parent))
        {
            if (string.Equals(ShellPathHelper.GetLocalizedFolderName(child), localizedName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }
}
