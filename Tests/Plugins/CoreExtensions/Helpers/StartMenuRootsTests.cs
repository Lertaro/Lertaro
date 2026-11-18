using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Helpers;

// The Desktop is an application root, and it has to be the Desktop the shell actually shows. With OneDrive's
// folder backup on, <profile>\Desktop does not exist and the real one sits under OneDrive -- a root list
// built only from profile-relative guesses silently scanned nothing there, so a portable app kept on the
// Desktop never ranked as an application. On a machine whose Desktop was never moved this passes either
// way; it is a regression test for the machines where it was.
[TestClass]
public sealed class StartMenuRootsTests
{
    [TestMethod]
    public void TheRootsIncludeTheDesktopTheShellReports()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
            Assert.Inconclusive("This account has no Desktop folder to look for.");

        CollectionAssert.Contains(
            StartMenuShortcutResolver.GetStartMenuRoots().ToList(),
            Path.GetFullPath(desktop));
    }
}
