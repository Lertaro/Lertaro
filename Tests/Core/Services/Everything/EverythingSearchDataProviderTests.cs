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
    public async Task QueryFolderSubtree_UnindexedFolder_ReturnsEmptyResult()
    {
        using var searchService = new SearchService();
        var provider = new EverythingSearchDataProvider(searchService);

        var request = new EverythingQueryRequest(
            ReplyHwnd: IntPtr.Zero,
            ReplyCopyDataMessage: 0,
            SearchFlags: 0,
            Offset: 0,
            MaxResults: 100,
            RequestFlags: 0x110,
            SortType: 0,
            SearchString: @"""Z:\NonExistentDriveXYZ\""",
            IsUnicode: true,
            IsQuery2: true);

        var result = await provider.ExecuteQueryAsync(request);

        Assert.AreEqual(0u, result.TotalItems);
        Assert.IsEmpty(result.Items);
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
