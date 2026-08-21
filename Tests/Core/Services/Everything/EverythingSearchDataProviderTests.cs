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
}
