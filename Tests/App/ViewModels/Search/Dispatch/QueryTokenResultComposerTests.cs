using Lertaro.App.ViewModels.Search.Dispatch;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

[TestClass]
public sealed class QueryTokenResultComposerTests
{
    [TestMethod]
    public void Compose_MoreThanDisplayLimit_ShowsFiftyResultsAndShowMore()
    {
        var files = CreateRows(51, "File");

        var result = QueryTokenResultComposer.Compose([], files, "jpg :.jpg");

        Assert.HasCount(QueryTokenResultComposer.DisplayLimit + 1, result);
        Assert.HasCount(QueryTokenResultComposer.DisplayLimit, result.Where(row => row.ResultKind == "File"));
        Assert.AreEqual("__SHOW_MORE__", result[^1].FullPath);
    }

    [TestMethod]
    public void Compose_AtDisplayLimit_DoesNotAddShowMore()
    {
        var files = CreateRows(QueryTokenResultComposer.DisplayLimit, "File");

        var result = QueryTokenResultComposer.Compose([], files, "jpg :.jpg");

        Assert.HasCount(QueryTokenResultComposer.DisplayLimit, result);
        Assert.IsFalse(result.Any(row => row.FullPath == "__SHOW_MORE__"));
    }

    [TestMethod]
    public void Compose_InstantResults_UsePartOfFiftyResultBudget()
    {
        var instantRows = CreateRows(2, "InstantResult");
        var files = CreateRows(50, "File");

        var result = QueryTokenResultComposer.Compose(instantRows, files, "jpg :.jpg");

        Assert.HasCount(QueryTokenResultComposer.DisplayLimit + 1, result);
        Assert.HasCount(2, result.Where(row => row.ResultKind == "InstantResult"));
        Assert.HasCount(48, result.Where(row => row.ResultKind == "File"));
        Assert.AreEqual("__SHOW_MORE__", result[^1].FullPath);
    }

    private static List<AppSearchResult> CreateRows(int count, string resultKind) =>
        Enumerable.Range(0, count)
            .Select(index => new AppSearchResult
            {
                Name = $"result-{index}",
                FullPath = $@"C:\results\result-{index}",
                ResultKind = resultKind
            })
            .ToList();
}
