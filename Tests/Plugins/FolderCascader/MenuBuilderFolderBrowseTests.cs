using Lertaro.Plugins.FolderCascader.Navigation;
using static Lertaro.Plugins.FolderCascader.Tests.MenuBuilderTestHelpers;

namespace Lertaro.Plugins.FolderCascader.Tests;

[TestClass]
public sealed class MenuBuilderFolderBrowseTests
{
    [TestMethod]
    public void GetMenuItems_FolderPageContinuesFromItsSessionSnapshot()
    {
        using var directory = new TempDirectory();
        for (var index = 0; index < FolderBrowseSnapshot.PageSize + 2; index++)
            File.WriteAllText(Path.Combine(directory.Path, $"item-{index:D3}.txt"), string.Empty);

        var provider = new Provider();
        var result = new FakeResult { FullPath = directory.Path };
        var firstPage = MenuBuilder.GetMenuItems(result, provider.AllocateHandle(directory.Path), provider).ToList();

        Assert.HasCount(FolderBrowseSnapshot.PageSize + 1, firstPage);
        var continuation = firstPage.Single(item => item.IsContinuation);
        Assert.IsTrue(continuation.HasSubMenu);

        File.WriteAllText(Path.Combine(directory.Path, "item-added-after-snapshot.txt"), string.Empty);
        var secondPage = MenuBuilder.GetMenuItems(result, continuation.SubMenuHandle, provider).ToList();

        Assert.HasCount(2, secondPage);
        Assert.IsFalse(secondPage.Any(item => item.IsContinuation));
        Assert.IsFalse(secondPage.Any(item => item.Text == "item-added-after-snapshot.txt"));
    }

    [TestMethod]
    public void GetMenuItems_FolderPageFiltersHiddenAndSystemEntries()
    {
        using var directory = new TempDirectory();
        var visible = Path.Combine(directory.Path, "visible.txt");
        var hidden = Path.Combine(directory.Path, "hidden.txt");
        var system = Path.Combine(directory.Path, "system.txt");
        File.WriteAllText(visible, string.Empty);
        File.WriteAllText(hidden, string.Empty);
        File.WriteAllText(system, string.Empty);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(system, FileAttributes.System);

        try
        {
            var provider = new Provider();
            var result = new FakeResult { FullPath = directory.Path };
            var items = MenuBuilder.GetMenuItems(result, provider.AllocateHandle(directory.Path), provider).ToList();

            Assert.HasCount(1, items);
            Assert.AreEqual("visible.txt", items[0].Text);
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Normal);
            File.SetAttributes(system, FileAttributes.Normal);
        }
    }
}
