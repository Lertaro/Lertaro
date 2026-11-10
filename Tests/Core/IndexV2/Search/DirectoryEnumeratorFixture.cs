using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.Services.Plugin.DirectoryIndex;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Shared setup for the directory-listing tests. Kept out of the test classes themselves so each one can
// stay under the repository's 300-line limit without either owning the sample tree.
internal static class DirectoryEnumeratorFixture
{
    // C:\
    //   Projects\        (2)
    //     readme.txt     (3)
    //     notes.md       (4)
    //     sub\           (5)
    //       deep.txt     (6)
    //   Downloads\       (7)
    //     install.exe    (8)
    public static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "notes.md", FileRecordFlags.None),
        new FileRecord(5, 2, "sub", FileRecordFlags.Directory),
        new FileRecord(6, 5, "deep.txt", FileRecordFlags.None),
        new FileRecord(7, 1, "Downloads", FileRecordFlags.Directory),
        new FileRecord(8, 7, "install.exe", FileRecordFlags.None),
    });

    public static (bool Resolved, List<SearchResult> Results) Enumerate(LiveIndexFixture fixture, string path,
        bool recursive = false, string filterPattern = "*", int limit = 0)
    {
        var results = new List<SearchResult>();
        var resolved = IndexV2Searcher.EnumerateDirectory(fixture.Index, path, recursive,
            FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern), limit, results.Add, CancellationToken.None);
        return (resolved, results);
    }

    public static string[] Paths(List<SearchResult> results) => results.Select(r => r.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
}
