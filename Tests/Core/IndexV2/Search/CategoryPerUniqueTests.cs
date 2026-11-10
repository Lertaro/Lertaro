using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Tests.IndexV2;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class CategoryPerUniqueTests
{
    [TestMethod]
    public void CategoryFilterAnswersEveryMatchingKindAfterUniqueLevelClassification()
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "alpha.exe", FileRecordFlags.None),
            new FileRecord(3, 1, "alpha.txt", FileRecordFlags.None),
            new FileRecord(4, 1, "alpha", FileRecordFlags.Directory),
        ]);

        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Application));
        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Document));
        Assert.AreEqual(1, Search(fixture, SearchCategoryFilter.Folder));
        Assert.AreEqual(3, Search(fixture, SearchCategoryFilter.All));
    }

    [TestMethod]
    [DataRow("alpha", false)]
    [DataRow("D:", false)]
    [DataRow("alpha", true)]
    [DataRow("D:", true)]
    public void FolderCategory_ExcludesBaseAndDeltaShortcuts(string query, bool mutate)
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "alpha.lnk", FileRecordFlags.None),
            new FileRecord(3, 1, "alpha-before.txt", FileRecordFlags.None),
            new FileRecord(4, 1, "alpha-folder", FileRecordFlags.Directory),
        ]);
        if (mutate)
            fixture.Index.Mutate((_, delta) =>
            {
                delta.Upsert(3, 1, "alpha-renamed.lnk", FileRecordFlags.None, 0, 0, 0, 0);
                delta.Upsert(5, 1, "alpha-added.lnk", FileRecordFlags.None, 0, 0, 0, 0);
            });

        var folders = Results(fixture, SearchCategoryFilter.Folder, query);
        Assert.HasCount(1, folders);
        Assert.AreEqual(@"D:\alpha-folder", folders[0].Path);
        Assert.IsTrue(folders[0].IsDir);
        Assert.HasCount(mutate ? 4 : 3, Results(fixture, SearchCategoryFilter.All, query));
    }

    [TestMethod]
    [DataRow("alpha")]
    [DataRow("D:")]
    public void SharedName_UsesEachRowsKindAndKeepsEveryHardLink(string query)
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "left", FileRecordFlags.Directory),
            new FileRecord(3, 1, "right", FileRecordFlags.Directory),
            new FileRecord(4, 2, "alpha.txt", FileRecordFlags.None),
            new FileRecord(4, 3, "alpha.txt", FileRecordFlags.None),
            new FileRecord(5, 1, "alpha.txt", FileRecordFlags.Directory),
        ]);

        var files = Results(fixture, SearchCategoryFilter.Document, query);
        Assert.HasCount(2, files);
        Assert.Contains(@"D:\left\alpha.txt", files.Select(row => row.Path));
        Assert.Contains(@"D:\right\alpha.txt", files.Select(row => row.Path));
        var folders = Results(fixture, SearchCategoryFilter.Folder, query, "*.pdf");
        Assert.HasCount(query == "D:" ? 3 : 1, folders);
        Assert.Contains(@"D:\alpha.txt", folders.Select(row => row.Path));
        Assert.IsTrue(folders.All(row => row.IsDir));
        Assert.IsEmpty(Results(fixture, SearchCategoryFilter.Document, query, "*.pdf"));
    }

    [TestMethod]
    [DataRow("alpha")]
    [DataRow("D:")]
    public void DeltaRenameAndAddition_AreClassifiedBeforeTheLimit(string query)
    {
        using var fixture = LiveIndexFixture.Build("D",
        [LiveIndexFixture.Root(), new FileRecord(2, 1, "alpha-before.txt", FileRecordFlags.None)]);
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(2, 1, "alpha.exe", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Upsert(3, 1, "alpha-added.txt", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Upsert(4, 1, "alpha-removed.txt", FileRecordFlags.None, 0, 0, 0, 0);
            delta.Remove(4);
        });

        var documents = Results(fixture, SearchCategoryFilter.Document, query, limit: 1);
        Assert.HasCount(1, documents);
        Assert.AreEqual("alpha-added.txt", documents[0].Name);
        var applications = Results(fixture, SearchCategoryFilter.Application, query, limit: 1);
        Assert.HasCount(1, applications);
        Assert.AreEqual("alpha.exe", applications[0].Name);
        Assert.IsEmpty(Results(fixture, SearchCategoryFilter.Folder, query));
    }

    [TestMethod]
    [DataRow("container alpha")]
    [DataRow("container alpha | missing")]
    public void AncestorTermFallback_DoesNotSpendFolderSlotsOnShortcuts(string query)
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "container", FileRecordFlags.Directory),
            new FileRecord(3, 2, "alpha.lnk", FileRecordFlags.None),
            new FileRecord(4, 2, "alpha.txt", FileRecordFlags.None),
            new FileRecord(5, 2, "alpha-folder", FileRecordFlags.Directory),
        ]);

        var folders = Results(fixture, SearchCategoryFilter.Folder, query, limit: 1);
        Assert.HasCount(1, folders);
        Assert.AreEqual(@"D:\container\alpha-folder", folders[0].Path);
        Assert.IsTrue(folders[0].IsDir);
    }

    private static int Search(LiveIndexFixture fixture, SearchCategoryFilter category) => Results(fixture, category).Count;

    private static List<SearchResult> Results(LiveIndexFixture fixture, SearchCategoryFilter category,
        string query = "alpha", string? fileNameFilter = null, int limit = 20)
    {
        var rows = new List<SearchResult>();
        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            NameSearch.SearchStreaming(
                snapshot, delta, FzfPattern.Parse(query),
                limit, rows.Add, CancellationToken.None,
                directoryFilterLower: null, fileNamePatterns: fileNameFilter == null ? null : [fileNameFilter], category: category);
            return null;
        });
        return rows;
    }
}
