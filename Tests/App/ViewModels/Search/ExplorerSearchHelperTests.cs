using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class ExplorerSearchHelperTests
{
    private static AppSearchResult Item(string path) => new() { FullPath = path, Name = path, ResultKind = "File" };

    [TestMethod]
    public void OrderByDirectoryTier_DirectChildrenPrecedeDescendantMatches()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Item(@"C:\Root\Child\nested.txt"), Item(@"C:\Root\direct.txt")],
            @"C:\Root");

        Assert.AreEqual(@"C:\Root\direct.txt", result[0].FullPath);
        Assert.AreEqual(@"C:\Root\Child\nested.txt", result[1].FullPath);
    }

    [TestMethod]
    public void OrderByDirectoryTier_PreservesExistingOrderWithinEachTier()
    {
        var result = ExplorerSearchHelper.OrderByDirectoryTier(
            [Item(@"C:\Root\Child\first-nested.txt"), Item(@"C:\Root\Child\second-nested.txt"), Item(@"C:\Root\first-direct.txt"), Item(@"C:\Root\second-direct.txt")],
            @"C:\Root");

        CollectionAssert.AreEqual(
            new[] { @"C:\Root\first-direct.txt", @"C:\Root\second-direct.txt", @"C:\Root\Child\first-nested.txt", @"C:\Root\Child\second-nested.txt" },
            result.Select(item => item.FullPath).ToArray());
    }
}
