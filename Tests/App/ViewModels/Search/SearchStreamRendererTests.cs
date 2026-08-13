using Lertaro.Core;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchStreamRendererTests
{
    private static SearchResult Result(string path) => new() { Path = path };

    [TestMethod]
    public void CopySnapshot_DefaultModeCopiesTheRequestedPrefixEveryTime()
    {
        var source = new List<SearchResult> { Result("1"), Result("2"), Result("3") };
        var copiedCount = 0;

        var first = SearchStreamRenderer.CopySnapshot(source, 2, false, ref copiedCount);
        var second = SearchStreamRenderer.CopySnapshot(source, int.MaxValue, false, ref copiedCount);

        CollectionAssert.AreEqual(new[] { "1", "2" }, first.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, second.Select(result => result.Path).ToList());
        Assert.AreEqual(0, copiedCount);
    }

    [TestMethod]
    public void CopySnapshot_BatchModeCopiesOnlyNewResults()
    {
        var source = new List<SearchResult> { Result("1"), Result("2"), Result("3") };
        var copiedCount = 0;

        var first = SearchStreamRenderer.CopySnapshot(source, 2, true, ref copiedCount);
        source.Add(Result("4"));
        var second = SearchStreamRenderer.CopySnapshot(source, 4, true, ref copiedCount);
        var unchanged = SearchStreamRenderer.CopySnapshot(source, int.MaxValue, true, ref copiedCount);

        CollectionAssert.AreEqual(new[] { "1", "2" }, first.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "3", "4" }, second.Select(result => result.Path).ToList());
        Assert.IsEmpty(unchanged);
        Assert.AreEqual(4, copiedCount);
    }
}
