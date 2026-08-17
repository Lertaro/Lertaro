using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class OpenedFolderMenuTests
{
    [TestMethod]
    public void BuildOpenedFoldersMenu_SortsByDisplayName()
    {
        var items = MenuBuilderContentExtensions.BuildOpenedFoldersMenu(new[]
        {
            @"D:\Work\Zebra",
            @"C:\Work\alpha"
        }, new Provider());

        CollectionAssert.AreEqual(new[] { "alpha", "Zebra" }, items.Select(item => item.Text).ToArray());
    }
}
