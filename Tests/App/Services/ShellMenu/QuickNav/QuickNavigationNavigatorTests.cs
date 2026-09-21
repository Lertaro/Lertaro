using Lertaro.App.Services.ShellMenu.QuickNav;

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
}
