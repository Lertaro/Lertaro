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
    public async Task QueryFolderSubtree_Folder_ReturnsCalculatedSizeItem()
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
            SearchString: @"""C:\Windows\""",
            IsUnicode: true,
            IsQuery2: true);

        var result = await provider.ExecuteQueryAsync(request);

        Assert.AreEqual(1u, result.TotalItems);
        Assert.HasCount(1, result.Items);
        Assert.AreEqual("Windows", result.Items[0].FileName);
    }
}
