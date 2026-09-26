using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class QuickNavigationNavigatorTests
{
    [TestMethod]
    public void ResolveNavigationPath_PreservesVirtualShellContainer()
    {
        const string path = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

        Assert.AreEqual(path, QuickNavigationNavigator.ResolveNavigationPath(path));
    }

    [TestMethod]
    public void ResolveNavigationPath_ResolvesPhysicalShellFolder()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.IsFalse(UserPathResolver.IsVirtualPath(QuickNavigationNavigator.ResolveNavigationPath("shell:Desktop")));
        Assert.AreEqual(path, QuickNavigationNavigator.ResolveNavigationPath("shell:Desktop"), ignoreCase: true);
    }

    [TestMethod]
    public void FolderBelongsToExplorerTabRoute_OnlySpeaksForExplorerOwnWindows()
    {
        var tabs = new DefaultFileManagerSetting { OpenFoldersInNewExplorerTabs = true };

        Assert.IsTrue(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, false, true, tabs));

        // A file, a virtual item and a third-party manager's window all keep navigating in place: the
        // option is about Explorer's tabs, and none of those has one to open.
        Assert.IsFalse(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(false, false, true, tabs));
        Assert.IsFalse(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, true, true, tabs));
        Assert.IsFalse(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, false, false, tabs));

        // Off is off, and a configured replacement file manager owns folder opens -- the same gate
        // FileExecutor applies before it asks for a tab, so a folder cannot end up in neither route.
        Assert.IsFalse(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, false, true, new DefaultFileManagerSetting()));
        Assert.IsFalse(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, false, true,
            new DefaultFileManagerSetting { OpenFoldersInNewExplorerTabs = true, Enabled = true, Path = @"D:\Files.exe" }));
        Assert.IsTrue(QuickNavigationNavigator.FolderBelongsToExplorerTabRoute(true, false, true,
            new DefaultFileManagerSetting { OpenFoldersInNewExplorerTabs = true, Enabled = true }));
    }
}
