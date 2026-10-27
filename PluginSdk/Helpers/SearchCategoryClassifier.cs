using System.IO;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Helpers;

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
/// an older client asks a newer service for. <see cref="Archive"/> was appended for the same reason.
///
/// This lives in the plugin SDK rather than in Core because three surfaces answer the same question and must
/// not drift: the engine's per-row filter, the quick window's filter tabs, and the Core Extensions plugin's
/// user-editable 自定义过滤器 (:@keyword) rows plus its sidebar type filters.
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
    Archive = 8,
}

/// <summary>
/// The one classification table: which extensions belong to which <see cref="SearchCategoryFilter"/>, and
/// the tests that decide it from a name or a path.
/// </summary>
/// <remarks>
/// Classified by extension rather than by shell association -- asking Windows what opens a file means a
/// registry read per row, on a path that runs once per candidate row. An extension no category claims is
/// returned as <c>null</c>, which keeps an unfamiliar file out of every narrow category instead of misfiling
/// it. The lists below are deliberately the union of what the 自定义过滤器 rules and the old sidebar sets
/// already covered (WPS and OpenDocument formats, <c>.jxl</c>, <c>.tar.gz</c>-style archives), so a row the
/// user could already ask for with <c>:@doc</c> is still a Document under the 文档 tab.
/// </remarks>
public static class SearchCategoryClassifier
{
    /// <summary>A shortcut is a pointer, not a kind: what it points at decides.</summary>
    public const string ShortcutExtension = ".lnk";

    /// <summary>
    /// The prefix of the placeholder path the host gives a row a plugin contributed, whose next segment is the
    /// provider's type name: <c>__SEARCHABLE_ITEM__:{ProviderType}:{Title}</c>.
    /// </summary>
    public const string SearchableItemPathPrefix = "__SEARCHABLE_ITEM__:";

    private static readonly string[] ApplicationExtensions =
    [
        ".exe", ".url", ".appref-ms", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com",
    ];

    private static readonly string[] DocumentExtensions =
    [
        ".txt", ".md", ".log", ".doc", ".docx", ".rtf", ".odt", ".pdf",
        ".xls", ".xlsx", ".csv", ".ods", ".ppt", ".pptx", ".odp",
        ".epub", ".mobi", ".one", ".vsdx", ".vsd",
        ".wps", ".et", ".dps", ".odf", ".odg", ".odb", ".eqp", ".mmx", ".tex",
        ".ini", ".conf",
    ];

    private static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".webp", ".svg", ".ico",
        ".tif", ".tiff", ".heic", ".heif", ".avif", ".raw", ".psd", ".ai", ".jxl",
    ];

    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga", ".m4a", ".wma", ".ape",
        ".opus", ".aiff", ".aif", ".mid", ".midi", ".dsf", ".dff",
    ];

    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".mts", ".3gp", ".rmvb", ".rm", ".vob", ".ogv",
    ];

    private static readonly string[] ArchiveExtensions =
    [
        ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".wim", ".esd",
    ];

    private static readonly HashSet<string> ApplicationSet = new(ApplicationExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DocumentSet = new(DocumentExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ImageSet = new(ImageExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AudioSet = new(AudioExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> VideoSet = new(VideoExtensions, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ArchiveSet = new(ArchiveExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The category a name or path belongs to, or <c>null</c> when it belongs to none -- including a
    /// <see cref="ShortcutExtension"/>, whose category only its resolved target can answer.
    /// </summary>
    /// <remarks>
    /// Takes a bare name as well as a full path: the engine asks per row while walking the index, where only
    /// the name is at hand and rebuilding the path would mean walking the parent chain for every candidate.
    /// Every test below is therefore name-safe.
    /// </remarks>
    public static SearchCategoryFilter? Classify(string? nameOrPath, bool isDirectory)
    {
        if (isDirectory)
            return SearchCategoryFilter.Folder;

        if (string.IsNullOrEmpty(nameOrPath))
            return null;

        var extension = Path.GetExtension(nameOrPath);

        // Windows exposes its settings pages in shapes that are not a file with an extension of their own:
        // an ms-settings: URI, the SystemSettings.exe host under ImmersiveControlPanel (an .exe, which the
        // application set below would otherwise claim), and the packaged Settings app's family name (whose
        // trailing "!Microsoft.Settings" reads as an extension). Only these three shapes can contradict what
        // their extension says, so the substring walk is limited to where they can appear: the engine asks
        // this per candidate row over a 200k+ scan, and a bare file name never has a colon in it.
        if (extension.Length == 0
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(ShortcutExtension, StringComparison.OrdinalIgnoreCase)
            || nameOrPath.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            if (IsSettingsEntry(nameOrPath))
                return SearchCategoryFilter.Settings;
        }

        // A shortcut is a pointer, not a kind, and only the caller can read what it points at.
        if (extension.Equals(ShortcutExtension, StringComparison.OrdinalIgnoreCase))
            return null;

        if (extension.Length == 0)
            return null;

        if (ApplicationSet.Contains(extension))
            return SearchCategoryFilter.Application;
        if (DocumentSet.Contains(extension))
            return SearchCategoryFilter.Document;
        if (ImageSet.Contains(extension))
            return SearchCategoryFilter.Image;
        if (AudioSet.Contains(extension))
            return SearchCategoryFilter.Audio;
        if (VideoSet.Contains(extension))
            return SearchCategoryFilter.Video;
        if (ArchiveSet.Contains(extension))
            return SearchCategoryFilter.Archive;

        // A shell namespace path is the one row kind no extension table can speak for, and every shape of it
        // carries a colon: ms-settings:, shell:, and a drive-qualified ImmersiveControlPanel path.
        return nameOrPath.IndexOf(':') >= 0 && IsSettingsEntry(nameOrPath)
            ? SearchCategoryFilter.Settings
            : null;
    }

    /// <summary>
    /// Whether a row survives <paramref name="filter"/>. <see cref="SearchCategoryFilter.All"/> accepts
    /// everything, including rows no narrower category claims.
    /// </summary>
    /// <remarks>
    /// A shortcut passes every filter, because the side that can resolve its target cannot narrow the walk
    /// and the side narrowing the walk cannot resolve it. Shortcuts are few next to a query's candidate set,
    /// so admitting them costs budget rather than correctness: the caller still files each one by its target,
    /// and a shortcut that turns out to belong elsewhere is dropped there instead of never arriving. Excluded
    /// rows of every other kind would simply be missing from the answer.
    /// </remarks>
    public static bool Matches(SearchCategoryFilter filter, string? nameOrPath, bool isDirectory)
    {
        if (filter == SearchCategoryFilter.All)
            return true;

        if (Classify(nameOrPath, isDirectory) == filter)
            return true;

        return !isDirectory
            && !string.IsNullOrEmpty(nameOrPath)
            && nameOrPath.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The category a search row belongs to, answering what a path alone cannot: which provider contributed
    /// it, whether it reports itself launchable, and where a shortcut points.
    /// </summary>
    /// <remarks>
    /// This is what a filter row tests and what the quick window's filter bar narrows by, so the sidebar and
    /// the bar cannot disagree about one row. A shortcut's target is read through
    /// <see cref="StartMenuShortcutResolver.ResolveTargetInfo"/>, which remembers its answers: resolving walks
    /// IShellLink, and callers run this per row on a batch the UI thread is about to paint.
    /// </remarks>
    public static SearchCategoryFilter? Categorize(ISearchResult? row)
    {
        if (row is null || string.IsNullOrEmpty(row.FullPath))
            return Classify(null, row?.IsDir == true);

        if (row.FullPath.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase))
        {
            var target = StartMenuShortcutResolver.ResolveTargetInfo(row.FullPath);
            if (target != null)
                return Classify(target.Path, target.IsDirectory);
        }

        // Order matters the other way for a launchable row: the app indexer marks a Start Menu shortcut's row
        // (and its resolved target) as an application, so the path's own kind has to be asked first or a
        // shortcut to a document would land under 应用 simply because the shell can launch it.
        return Classify(row.FullPath, row.IsDir)
            ?? (row.IsApplication ? SearchCategoryFilter.Application : null);
    }

    /// <summary>
    /// The same category as a 自定义过滤器 rule string, for callers that express a category as globs
    /// (see <c>CustomFilterQueryTokenProvider</c>). Empty for the categories no glob can answer.
    /// </summary>
    public static string RuleOf(SearchCategoryFilter filter) => filter switch
    {
        SearchCategoryFilter.Application => Rule(ApplicationExtensions),
        SearchCategoryFilter.Document => Rule(DocumentExtensions),
        SearchCategoryFilter.Image => Rule(ImageExtensions),
        SearchCategoryFilter.Audio => Rule(AudioExtensions),
        SearchCategoryFilter.Video => Rule(VideoExtensions),
        SearchCategoryFilter.Archive => Rule(ArchiveExtensions),
        _ => string.Empty,
    };

    private static string Rule(string[] extensions) => string.Join("; ", extensions.Select(ext => "*" + ext));

    private static bool IsSettingsEntry(string nameOrPath) =>
        nameOrPath.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)
        || nameOrPath.Contains("immersivecontrolpanel", StringComparison.OrdinalIgnoreCase)
        || nameOrPath.Contains("SystemSettings.exe", StringComparison.OrdinalIgnoreCase)
        // A plugin-contributed searchable item has no file behind it, so the host gives it a placeholder path
        // of the shape __SEARCHABLE_ITEM__:{ProviderType}:{Title} (see SearchableItemMapper). Every entry the
        // system-settings provider contributes is a settings entry -- Bluetooth, Device Manager, user account
        // control, the lot -- and they also report themselves as applications, so the provider named in that
        // placeholder is what tells them apart from real programs.
        || (nameOrPath.StartsWith(SearchableItemPathPrefix, StringComparison.OrdinalIgnoreCase)
            && nameOrPath[SearchableItemPathPrefix.Length..]
                .Split(':')[0]
                .Contains("SystemSettings", StringComparison.OrdinalIgnoreCase));
}
