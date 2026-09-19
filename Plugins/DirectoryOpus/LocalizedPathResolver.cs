using System.IO;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.DirectoryOpus;

/// <summary>
/// Turns a LOCALIZED display path (<c>C:\用户\testuser\AppData\Local\Temp</c>) back into the real
/// filesystem path (<c>C:\Users\testuser\AppData\Local\Temp</c>).
/// </summary>
/// <remarks>
/// Directory Opus localizes the folder names it captures from windows -- a file-display container's
/// window text, and the <c>display_path</c> attribute of its XML output, both read <c>C:\用户\...</c>
/// for <c>C:\Users\...</c> on a Chinese Windows. That spelling is not a path that exists, so anything
/// built from it is unusable. Opus's own XML carries the real path in each element's text (which is why
/// <see cref="DopusRtPathQuery"/> reads that), but the window scrape has only the localized title, so it
/// needs the translation back.
/// </remarks>
/// <remarks>
/// The translation walks the real directory tree one segment at a time, comparing each child's
/// shell display name -- <see cref="ShellPathHelper.GetLocalizedFolderName"/>, the same call Explorer
/// itself uses -- against the localized segment, and falls back to the original path the moment a
/// segment cannot be matched. A segment whose real name is spelled exactly as reported is taken
/// directly, so a path with no localized component in it (the common case) never enumerates anything.
/// No hand-maintained table of folder names is involved, so this works in every display language.
/// ponytail: a localized segment costs one directory listing plus a SHGetFileInfo per child, so the
/// ceiling is (path depth x entries in the containing folder) syscalls. That is paid only by the window
/// scrape, only for a path that does not exist as reported, so it is off every hot path; if it ever
/// moves onto one, cache the display names of a directory instead of re-reading them per lookup.
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
