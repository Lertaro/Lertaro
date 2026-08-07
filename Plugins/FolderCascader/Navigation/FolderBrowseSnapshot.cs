using System.IO;

namespace Lertaro.Plugins.FolderCascader.Navigation;

// A quick-navigation popup owns one snapshot per physical folder for its short-lived session. This
// avoids rescanning and resorting a large folder for every continuation page while preserving the
// directories-first, case-insensitive order the cascade has always exposed.
internal sealed class FolderBrowseSnapshot
{
    internal const int PageSize = 100;
    private readonly IReadOnlyList<FolderBrowseEntry> _entries;

    private FolderBrowseSnapshot(IReadOnlyList<FolderBrowseEntry> entries) => _entries = entries;

    internal int Count => _entries.Count;

    internal IReadOnlyList<FolderBrowseEntry> GetPage(int offset) =>
        offset < 0 || offset >= _entries.Count
            ? Array.Empty<FolderBrowseEntry>()
            : _entries.Skip(offset).Take(PageSize).ToList();

    internal static FolderBrowseSnapshot Load(string path)
    {
        var directories = Directory.GetDirectories(path)
            .Where(IsVisible)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FolderBrowseEntry(path, IsDirectory: true));
        var files = Directory.GetFiles(path)
            .Where(IsVisible)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new FolderBrowseEntry(path, IsDirectory: false));
        return new FolderBrowseSnapshot(directories.Concat(files).ToList());
    }

    private static bool IsVisible(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) == 0;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record FolderBrowseEntry(string Path, bool IsDirectory);
