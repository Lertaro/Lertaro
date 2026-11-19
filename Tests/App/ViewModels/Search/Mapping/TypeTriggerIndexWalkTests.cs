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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PriorityRootSupplement_FindsTheTriggeredKindBeyondBothMixedCuts(bool wantedIsDirectory)
    {
        const string priorityRoot = @"Z:\Promoted";
        // SearchService gives both budgets 4x headroom: 1000 for the main query with high-priority
        // rules, and SearchStreamRenderer's 50 per promoted root.
        const int mainLimit = 1000 * 4;
        const int supplementLimit = 50 * 4;
        var records = new List<FileRecord>
        {
            LiveIndexFixture.Root(),
            new(2, 1, "Promoted", FileRecordFlags.Directory),
        };
        var wantedFlags = wantedIsDirectory ? FileRecordFlags.Directory : FileRecordFlags.None;
        var otherFlags = wantedIsDirectory ? FileRecordFlags.None : FileRecordFlags.Directory;
        for (var i = 0; i < Crowd; i++)
            records.Add(new((ulong)i + 3, 1, wantedIsDirectory ? $"report{i:D4}" : $"report{i:D4}.txt", wantedFlags));
        for (var i = 0; i < supplementLimit + 50; i++)
            records.Add(new((ulong)(Crowd + i + 3), 2, wantedIsDirectory ? $"report{i:D4}.txt" : $"report{i:D4}", otherFlags));
        var wantedName = wantedIsDirectory ? "xreportx-preferred" : "xreportx-preferred.txt";
        records.Add(new((ulong)(Crowd + supplementLimit + 53), 2, wantedName, wantedFlags));
        using var fixture = LiveIndexFixture.Build("Z", records);
        var trigger = wantedIsDirectory ? SearchResultTypePriority.FoldersTypeId : SearchResultTypePriority.FilesTypeId;
        var category = SearchResultTypePriority.IndexCategoryFor(trigger);

        var main = Walk(fixture, category, mainLimit);
        var mixedSupplement = Walk(fixture, SearchCategoryFilter.All, supplementLimit, priorityRoot);
        Assert.HasCount(mainLimit, main);
        Assert.DoesNotContain(wantedName, main.Select(r => r.Name));
        Assert.HasCount(supplementLimit, mixedSupplement);
        Assert.IsTrue(mixedSupplement.All(r => r.IsDir != wantedIsDirectory),
            "The old supplemental query spends its entire budget on the wrong kind.");

        var supplement = Walk(fixture, category, supplementLimit, priorityRoot);
        var recovered = Assert.ContainsSingle(supplement);
        Assert.AreEqual(wantedName, recovered.Name);
        Assert.AreEqual(wantedIsDirectory, recovered.IsDir);
        Assert.Contains(wantedName, main.Concat(supplement)
            .Where(r => SearchResultMapper.InTriggeredFileBucket(r.IsDir, trigger))
            .Select(r => r.Name));
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

    private static List<SearchResult> Walk(LiveIndexFixture fixture, SearchCategoryFilter category, int limit, string? directory = null)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "report", limit, results.Add, CancellationToken.None,
            directoryFilter: directory, category: category);
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
