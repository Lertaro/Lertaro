namespace Lertaro.Core.Wire;

/// <summary>
/// Which kind of entry a search should return. <see cref="All"/> is the default and asks for everything.
/// </summary>
/// <remarks>
/// Carried on the request rather than applied by the caller, because the limit on a search is applied inside
/// the index walk: the caller receives at most a small multiple of <c>Limit</c> rows, ranked across every
/// kind. A broad query matching tens of thousands of entries fills that budget with whatever ranks highest
/// overall, so a category whose rows rank further down arrives with a handful of results or none -- even
/// though the engine matched plenty of them. Filtering at the source makes the budget apply inside the
/// category, which is the set the caller actually asked for.
///
/// Values are explicit and stable: they travel over the pipe, so renumbering them would silently change what
/// an older client asks a newer service for.
/// </remarks>
public enum SearchCategoryFilter : byte
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
/// The engine-side test for <see cref="SearchCategoryFilter"/>, applied per row while the index is being
/// walked.
/// </summary>
/// <remarks>
/// Deliberately recognises only what a path can answer on its own: a directory, or a file whose extension
/// belongs to the category. Shortcuts and Windows settings pages are NOT resolved here -- that needs the
/// shell, which is the caller's side of the pipe -- so they are left to pass an <see cref="SearchCategoryFilter.All"/>
/// request and be filed by the caller. That asymmetry is intentional: being conservative here can only leave a
/// row out of a narrower category, never misfile it, and the caller's own classification is what the list
/// shows either way.
/// </remarks>
public static class SearchCategoryClassifier
{
    private static readonly HashSet<string> ApplicationExtensions = new(StringComparer.OrdinalIgnoreCase)
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
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".mts", ".3gp", ".rmvb", ".rm", ".vob", ".ogv",
    };

    /// <summary>
    /// Whether a row belongs to <paramref name="filter"/>. <see cref="SearchCategoryFilter.All"/> accepts
    /// everything, including rows no narrower category claims.
    /// </summary>
    public static bool Matches(SearchCategoryFilter filter, string? path, bool isDirectory)
    {
        if (filter == SearchCategoryFilter.All)
            return true;

        if (isDirectory)
            return filter == SearchCategoryFilter.Folder;

        if (string.IsNullOrEmpty(path))
            return false;

        // Windows exposes its settings pages in several shapes, and none of them is a plain file with an
        // extension of its own:
        //   * an ms-settings: URI, which has no extension at all;
        //   * the SystemSettings.exe host under ImmersiveControlPanel, which is an .exe and would otherwise be
        //     filed as an application;
        //   * a .lnk pointing at either, which only the caller can resolve.
        // All three have to be recognised here, before the extension guards below would reject or misfile them.
        if (filter == SearchCategoryFilter.Settings)
        {
            if (path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\ImmersiveControlPanel\", StringComparison.OrdinalIgnoreCase)
                || path.Contains("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase)
                || path.Contains("SystemSettings.exe", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return false;

        return filter switch
        {
            SearchCategoryFilter.Folder => false,
            SearchCategoryFilter.Application => ApplicationExtensions.Contains(extension),
            SearchCategoryFilter.Document => DocumentExtensions.Contains(extension),
            SearchCategoryFilter.Image => ImageExtensions.Contains(extension),
            SearchCategoryFilter.Audio => AudioExtensions.Contains(extension),
            SearchCategoryFilter.Video => VideoExtensions.Contains(extension),
            // A settings entry may also be a plain .lnk pointing at an ms-settings: URI, and only the caller
            // can resolve that. Shortcuts are few next to a query's result set, so admitting them here does
            // not distort the budget this filter exists to protect; the caller files them.
            SearchCategoryFilter.Settings => extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }
}