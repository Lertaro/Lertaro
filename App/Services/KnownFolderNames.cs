using System.IO;

namespace Lertaro.App.Services;

/// <summary>
/// The user's own profile folders under the names Windows shows for them, so a query in Chinese (or its
/// pinyin initials) can reach a folder whose on-disk name is English.
/// </summary>
/// <remarks>
/// The index stores the real name: the folder on disk is "Desktop", "Downloads", "Music". Windows displays
/// "桌面", "下载", "音乐" from a localized resource that lives in the shell's localised resource strings, and
/// that text appears nowhere in the index -- so the Chinese name and its pinyin matched nothing, while typing
/// the English name always worked. Reported as "the quick-locate cannot find 桌面/下载/音乐".
///
/// The names are written out rather than asked for. SHGetFileInfo with SHGFI_DISPLAYNAME answers with the
/// on-disk name for a plain path (it does not walk the shell's localized folder names), and parsing the
/// resource reference in desktop.ini means expanding a "@%SystemRoot%\...,-NNNN" string out of shell32.dll.
/// These folders have a small, fixed set of names on every Chinese Windows, and the English spelling is kept
/// alongside them so an English install still resolves.
///
/// Nothing is added to the index: these are injected as extra rows at match time, and only while the query is
/// short enough to plausibly be naming one of them.
/// </remarks>
internal static class KnownFolderNames
{
    /// <summary>One profile folder: its real path and the names it is known by.</summary>
    internal readonly record struct Folder(string Path, string DisplayName, string[] Names);

    // Long enough to spell a folder name, short enough that a real file query is never shadowed by these.
    private const int MaxQueryLength = 24;

    private static readonly object Gate = new();
    private static List<Folder>? _folders;

    /// <summary>Folders whose name matches <paramref name="query"/>. Empty for an empty query.</summary>
    public static IReadOnlyList<Folder> Match(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > MaxQueryLength)
            return Array.Empty<Folder>();

        List<Folder> folders;
        lock (Gate)
            folders = _folders ??= Load();

        if (folders.Count == 0)
            return Array.Empty<Folder>();

        var fuzzy = Core.SearchIndex.FuzzyQuery.Parse(query);
        if (fuzzy.IsEmpty)
            return Array.Empty<Folder>();

        var hits = new List<Folder>();
        foreach (var folder in folders)
        {
            foreach (var name in folder.Names)
            {
                if (fuzzy.IsMatch(name) || fuzzy.IsAliasMatch(name))
                {
                    hits.Add(folder);
                    break;
                }
            }
        }

        return hits;
    }

    private static List<Folder> Load()
    {
        var result = new List<Folder>(8);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, display, names) in Candidates)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                continue;
            if (!seen.Add(path))
                continue;

            result.Add(new Folder(path, display, names));
        }

        return result;
    }

    private static (string Path, string Display, string[] Names)[] Candidates
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return
            [
                (Special(Environment.SpecialFolder.DesktopDirectory), "桌面", ["桌面", "zhuomian", "desktop"]),
                (Path.Combine(profile, "Downloads"), "下载", ["下载", "xiazai", "downloads"]),
                (Special(Environment.SpecialFolder.MyDocuments), "文档", ["文档", "wendang", "documents"]),
                (Special(Environment.SpecialFolder.MyPictures), "图片", ["图片", "tupian", "pictures"]),
                (Special(Environment.SpecialFolder.MyMusic), "音乐", ["音乐", "yinyue", "music"]),
                (Special(Environment.SpecialFolder.MyVideos), "视频", ["视频", "shipin", "videos"]),
                (profile, "用户", ["用户", "yonghu", "user", "profile"]),
            ];
        }
    }

    private static string Special(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return string.Empty;
        }
    }
}
