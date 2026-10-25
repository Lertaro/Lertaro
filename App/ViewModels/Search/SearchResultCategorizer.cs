using System.IO;
using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Search;

/// <summary>The quick window's result filters. <see cref="All"/> is the default and shows everything.</summary>
public enum SearchResultCategory
{
    All = 0,
    Folder = 1,
    Application = 2,
    Settings = 3,
    Document = 4,
    Image = 5,
    Audio = 6,
    Video = 7,
}

/// <summary>
/// Which filter a result belongs to. One row belongs to at most one category; a row that belongs to none
/// (an archive, a source file, a plugin's own instant answer) is shown only under <see cref="SearchResultCategory.All"/>.
/// </summary>
/// <remarks>
/// A shortcut is filed by what it points AT, not by being a shortcut: a .lnk is a pointer, and a pointer to a
/// document is a document. Filing every .lnk as an application put document and folder shortcuts in the Apps
/// tab purely because the shell calls them launchable.
///
/// Classification is by extension rather than by shell association: asking Windows what opens a file means a
/// registry read per row, on the paint path, for a value it may not even have. The extension sets below are
/// the common cases the user asked for; an unfamiliar extension falls through to "All" instead of being
/// misfiled.
/// </remarks>
public static class SearchResultCategorizer
{
    // Launchable things. "Application" covers what the app provider reports as one; these cover the rest --
    // a portable executable the provider never indexed, a console script.
    private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".url", ".appref-ms", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com",
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".log", ".doc", ".docx", ".rtf", ".odt", ".pdf",
        ".xls", ".xlsx", ".csv", ".ods", ".ppt", ".pptx", ".odp",
        ".epub", ".mobi", ".one", ".vsdx", ".vsd",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".webp", ".svg", ".ico",
        ".tif", ".tiff", ".heic", ".heif", ".avif", ".raw", ".psd", ".ai",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".ape",
        ".opus", ".aiff", ".aif", ".mid", ".midi", ".dsf", ".dff",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts",
        ".mpg", ".mpeg", ".rmvb", ".rm", ".3gp", ".vob", ".m2ts",
    };

    /// <summary>
    /// The filter a row belongs to, or null when it belongs to none.
    /// </summary>
    public static SearchResultCategory? Categorize(AppSearchResult result)
    {
        // Every entry the system-settings provider contributes is a settings entry: Bluetooth, Device Manager,
        // user account control, the lot. They are searchable items whose path is the GodMode namespace rather
        // than a file, and they also report themselves as applications, so the provider they came from is the
        // only reliable way to tell them apart from real applications.
        if (result.SourceProvider is Lertaro.PluginSdk.Abstractions.Plugins.ISearchableItemProvider settingsProvider
            && SearchResultTypePriority.GetProviderTypeId(settingsProvider)
                .Contains("SystemSettings", StringComparison.OrdinalIgnoreCase))
            return SearchResultCategory.Settings;

        // A plugin naming its own kind wins outright. Windows settings pages arrive this way: searchable
        // items with no path at all, so every path-based test below has nothing to read. This has to come
        // before the IsApplication check, because those rows also report as applications and would be filed
        // under Apps instead.
        if (string.Equals(result.ResultKind, "Settings", StringComparison.OrdinalIgnoreCase))
            return SearchResultCategory.Settings;

        // A shortcut is classified by its target first, whichever way the row got here. The app provider
        // marks the ones it indexed as applications, and the shell calls every .lnk launchable -- but a
        // shortcut to a document or to a folder is not an application.
        if (result.FullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ShortcutTargetResolver.Resolve(result.FullPath);
            if (!string.IsNullOrEmpty(target))
            {
                // A settings page is an ms-settings: URI: it has no extension, so the path classifier below
                // would file it as "no category". Only resolving the shortcut reveals it, which is why this
                // check lives here and not in the extension sets.
                if (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
                    return SearchResultCategory.Settings;

                return CategorizePath(target, isDirectory: Directory.Exists(target));
            }
        }

        // Settings also arrive as the SystemSettings.exe host under ImmersiveControlPanel -- an .exe, which
        // every application set below would otherwise claim -- or as the URI itself with no shortcut between.
        if (result.FullPath.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)
            || result.FullPath.Contains(@"\ImmersiveControlPanel\", StringComparison.OrdinalIgnoreCase)
            // The packaged Settings app is addressed as shell:AppsFolder\windows.immersivecontrolpanel_xxx --
            // a package family name with no backslash around it, which is the shape that actually ships.
            || result.FullPath.Contains("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase)
            || result.FullPath.Contains("SystemSettings.exe", StringComparison.OrdinalIgnoreCase))
            return SearchResultCategory.Settings;

        if (result.IsDir)
            return SearchResultCategory.Folder;

        if (result.IsApplication)
            return SearchResultCategory.Application;

        // A plugin instant answer (a calculator result, a command) is not a file and has no extension to
        // classify; leaving it unclassified keeps it out of every filter but All, where it belongs.
        if (result.IsInstantResult)
            return null;

        return CategorizePath(result.FullPath, isDirectory: false);
    }

    /// <summary>Whether a row survives the given filter. <see cref="SearchResultCategory.All"/> keeps everything.</summary>
    public static bool Matches(SearchResultCategory category, AppSearchResult result) =>
        category == SearchResultCategory.All || Categorize(result) == category;

    private static SearchResultCategory? CategorizePath(string path, bool isDirectory)
    {
        if (isDirectory)
            return SearchResultCategory.Folder;

        var extension = ExtensionOf(path);
        if (extension.Length == 0)
            return null;

        if (LaunchableExtensions.Contains(extension))
            return SearchResultCategory.Application;
        if (DocumentExtensions.Contains(extension))
            return SearchResultCategory.Document;
        if (ImageExtensions.Contains(extension))
            return SearchResultCategory.Image;
        if (AudioExtensions.Contains(extension))
            return SearchResultCategory.Audio;
        if (VideoExtensions.Contains(extension))
            return SearchResultCategory.Video;

        return null;
    }

    // Kept as a string rather than a span: the sets below are string-keyed, and a span-keyed lookup would
    // need a hand-written comparison per set to save one small allocation on a path that runs once per row.
    private static string ExtensionOf(string path)
    {
        var name = path.AsSpan();
        var separator = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (separator >= 0)
            name = name[(separator + 1)..];

        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..].ToString();
    }
}
