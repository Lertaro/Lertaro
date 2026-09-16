using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Shared setup for the searcher tests (see IndexV2SearcherTests / IndexV2SearcherPathModeTests).
internal static class IndexV2SearcherFixture
{
    // C:\
    //   Projects\      (2)
    //     readme.txt   (3)
    //     notes.md     (4)
    //   Downloads\     (5)
    //     install.exe  (6)
    public static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "notes.md", FileRecordFlags.None),
        new FileRecord(5, 1, "Downloads", FileRecordFlags.Directory),
        new FileRecord(6, 5, "install.exe", FileRecordFlags.None),
    });

    public static List<SearchResult> RunSearch(LiveIndexFixture fixture, string query, int limit = 10, string? directoryFilter = null)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None, directoryFilter: directoryFilter);
        return results;
    }
}
