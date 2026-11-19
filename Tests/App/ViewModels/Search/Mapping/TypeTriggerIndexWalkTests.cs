using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.Tests.IndexV2;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

// The Files and Folders triggers used to cut the mixed list to the budget first and drop the other kind after,
// so a page of folders left a Files search empty however many files matched further down. Each case runs the
// trigger the way the quick window does: its index category, the real index walk at both quick-window budgets,
// then the mapper's own per-row type filter.
[TestClass]
public sealed class TypeTriggerIndexWalkTests
{
    // More rows of the wrong kind than either budget, even with the service's 4x headroom on 1000.
    private const int Crowd = 4100;

    [TestMethod]
    [DataRow(51)]
    [DataRow(1000)]
    public void FilesTrigger_FindsTheFilesBehindMoreFoldersThanTheBudget(int limit)
    {
        using var fixture = Corpus(crowdIsDirectory: true, wantedName: i => $"xreportx-{i}.txt", wantedIsDirectory: false);

        AssertTriggerFindsTheWantedRows(fixture, SearchResultTypePriority.FilesTypeId, limit, wantedIsDirectory: false);
    }

    [TestMethod]
    [DataRow(51)]
    [DataRow(1000)]
    public void FoldersTrigger_FindsTheFoldersBehindMoreFilesThanTheBudget(int limit)
    {
        using var fixture = Corpus(crowdIsDirectory: false, wantedName: i => $"xreportx-{i}", wantedIsDirectory: true);

        AssertTriggerFindsTheWantedRows(fixture, SearchResultTypePriority.FoldersTypeId, limit, wantedIsDirectory: true);
    }

    [TestMethod]
    public void OtherTriggers_LeaveTheWalkUnnarrowed()
    {
        Assert.AreEqual(SearchCategoryFilter.All, SearchResultTypePriority.IndexCategoryFor(null));
        Assert.AreEqual(SearchCategoryFilter.All, SearchResultTypePriority.IndexCategoryFor("apps"));
    }

    private static void AssertTriggerFindsTheWantedRows(LiveIndexFixture fixture, string triggeredTypeId, int limit, bool wantedIsDirectory)
    {
        // The corpus must actually show the defect: unnarrowed, the cut holds none of the wanted kind.
        var mixed = Walk(fixture, SearchCategoryFilter.All, limit);
        Assert.IsFalse(mixed.Any(r => r.IsDir == wantedIsDirectory), "the crowd should fill the unnarrowed cut");

        var kept = Walk(fixture, SearchResultTypePriority.IndexCategoryFor(triggeredTypeId), limit)
            .Where(r => SearchResultMapper.InTriggeredFileBucket(r.IsDir, triggeredTypeId))
            .ToList();

        Assert.HasCount(3, kept);
        Assert.IsTrue(kept.All(r => r.IsDir == wantedIsDirectory && r.Name.StartsWith("xreportx-", StringComparison.Ordinal)));
    }

    private static List<SearchResult> Walk(LiveIndexFixture fixture, SearchCategoryFilter category, int limit)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "report", limit, results.Add, CancellationToken.None, category: category);
        return results;
    }

    // The crowd starts with the query, so it outranks the three wanted rows, whose match sits mid-word.
    private static LiveIndexFixture Corpus(bool crowdIsDirectory, Func<int, string> wantedName, bool wantedIsDirectory)
    {
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        var crowdFlags = crowdIsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None;
        for (var i = 0; i < Crowd; i++)
            records.Add(new((ulong)i + 2, 1, crowdIsDirectory ? $"report{i:D4}" : $"report{i:D4}.md", crowdFlags));
        for (var i = 0; i < 3; i++)
            records.Add(new((ulong)(Crowd + i + 2), 1, wantedName(i), wantedIsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None));
        return LiveIndexFixture.Build("Z", records);
    }
}
