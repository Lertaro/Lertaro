using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Services.Everything;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Everything;

[TestClass]
public class EverythingSearchDataProviderTests
{
    [TestMethod]
    public void RunCountTracking_MaintainsStateProperly()
    {
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        var file = @"C:\Tools\tool.exe";
        Assert.AreEqual(0u, provider.GetRunCount(file));

        provider.SetRunCount(file, 5);
        Assert.AreEqual(5u, provider.GetRunCount(file));

        var next = provider.IncrementRunCount(file);
        Assert.AreEqual(6u, next);
        Assert.AreEqual(6u, provider.GetRunCount(file));
    }

    [TestMethod]
    public void RunCountTracking_PastTheEntryCap_StoresNoNewNames()
    {
        // The IPC surface takes these from any local process, so an unbounded map is a memory-exhaustion
        // vector; past the cap a name nobody has run through Lertaro is dropped rather than kept.
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        for (var i = 0; i < EverythingSearchDataProvider.MaxRunHistoryEntries; i++)
            provider.SetRunCount($@"C:\Tools\tool{i}.exe", 1);

        var latecomer = @"C:\Tools\latecomer.exe";
        provider.SetRunCount(latecomer, 7);
        Assert.AreEqual(0u, provider.GetRunCount(latecomer));
        Assert.AreEqual(0u, provider.IncrementRunCount(latecomer));

        var alreadyStored = @"C:\Tools\tool0.exe";
        provider.SetRunCount(alreadyStored, 9);
        Assert.AreEqual(9u, provider.GetRunCount(alreadyStored), "an entry already in the map is still updated");
    }

    // The answers a folder-size client can get, decided by the index rows rather than by this machine. The
    // provider's query path itself cannot run here: it reads the index through the elevated service's pipe,
    // and with the service stopped the connect times out -- which reached this test as "The operation has
    // timed out." instead of as "the folder is not indexed". EverythingQueryParserTests covers the query
    // string that routes here (a quoted path with a trailing separator).
    [TestMethod]
    public void AnswerFolderSubtreeSize_UnindexedFolder_AnswersNothing()
    {
        var result = EverythingSearchDataProvider.AnswerFolderSubtreeSize(@"Z:\NonExistentDriveXYZ\", []);

        Assert.AreEqual(0u, result.TotalItems);
        Assert.IsEmpty(result.Items);
    }

    [TestMethod]
    public void AnswerFolderSubtreeSize_IndexedFolder_AnswersOneRowWithItsWholeSubtreeSize()
    {
        var result = EverythingSearchDataProvider.AnswerFolderSubtreeSize(@"C:\Projects\atlas\",
        [
            new SpaceIndexEntry(@"C:\Projects\atlas\report.docx", "report.docx", 1024, false, false),
            new SpaceIndexEntry(@"C:\Projects\atlas\raw\photo.png", "photo.png", 2048, false, false),
        ]);

        var item = Assert.ContainsSingle(result.Items);
        Assert.AreEqual(1u, result.TotalItems);
        Assert.AreEqual("atlas", item.FileName);
        Assert.AreEqual(@"C:\Projects", item.Path);
        Assert.AreEqual(3072L, item.Size, "the row carries the subtree's total, not one entry's size");
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_RootDrivesQuery_ReturnsDrivesList()
    {
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        var request = new EverythingQueryRequest(
            ReplyHwnd: IntPtr.Zero,
            ReplyCopyDataMessage: 0,
            SearchFlags: 0,
            Offset: 0,
            MaxResults: 100,
            RequestFlags: EverythingIpcConstants.RequestFileName | EverythingIpcConstants.RequestPath,
            SortType: 0,
            SearchString: "root:",
            IsUnicode: true,
            IsQuery2: true);

        var result = await provider.ExecuteQueryAsync(request);

        Assert.IsNotNull(result);
        Assert.IsGreaterThanOrEqualTo(1u, result.TotalItems);
        Assert.IsTrue(result.Items.Any(i => i.IsDrive));
    }
}
