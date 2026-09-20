using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class OpenedFolderMenuTests
{
    // The reported order is kept, because it carries something the folder names do not: Directory Opus
    // hands the tabs back with the tab the user is looking at (the one with active_tab) before that
    // group's other tabs, and sorting the menu by name -- what this used to do -- is exactly what put the
    // focused tab in the middle of the list.
    [TestMethod]
    public void BuildOpenedFoldersMenu_KeepsTheReportedOrder()
    {
        var items = MenuBuilderContentExtensions.BuildOpenedFoldersMenu(new[]
        {
            @"D:\Work\Zebra",
            @"C:\Work\alpha"
        }, new Provider());

        CollectionAssert.AreEqual(new[] { "Zebra", "alpha" }, items.Select(item => item.Text).ToArray());
    }
}
