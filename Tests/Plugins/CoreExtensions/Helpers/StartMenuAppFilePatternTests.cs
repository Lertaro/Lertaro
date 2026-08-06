using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Helpers;

// The app-file pattern and the app-file test have to mean the same thing. The pattern is what the host
// is asked to enumerate; ShouldIndex is what the results are then put through. A pattern narrower than
// the test hides a whole kind of app with nothing to say so; a wider one drags back files that are
// dropped a moment later, which is only waste but looks identical from the outside.
[TestClass]
public sealed class StartMenuAppFilePatternTests
{
    private static IEnumerable<string> Extensions() =>
        StartMenuShortcutResolver.AppFilePattern.Split(';').Select(p => p.TrimStart('*'));

    [TestMethod]
    public void EveryExtensionThePatternAsksForIsOneShouldIndexAccepts()
    {
        foreach (var extension in Extensions())
            Assert.IsTrue(StartMenuShortcutResolver.ShouldIndex("C:\\Apps\\Thing" + extension), $"pattern asks for {extension} but ShouldIndex drops it");
    }

    [TestMethod]
    public void ThePatternCoversTheKindsThatActuallyAppearInAStartMenu() =>
        // Not an exhaustive list of what ShouldIndex takes -- these are the four an installer really
        // leaves behind, and losing any of them silently is the failure worth catching.
        CollectionAssert.IsSubsetOf(new[] { ".lnk", ".url", ".exe", ".appref-ms" }, Extensions().ToList());

    [TestMethod]
    public void ThePatternIsInTheFormTheHostEnumeratorExpects()
    {
        // ';'-separated Win32 wildcards -- see DirectoryIndexerService.EnumerateDirectoryAsync.
        foreach (var part in StartMenuShortcutResolver.AppFilePattern.Split(';'))
        {
            Assert.StartsWith("*.", part);
            Assert.IsGreaterThan(2, part.Length);
        }
    }

    // desktop.ini carries a .ini extension, which no pattern above asks for -- but ShouldIndex names it
    // explicitly, and that belt-and-braces has to stay: the enumeration is only the first of the two
    // filters, and a folder-view file is never an app whichever way it arrives.
    [TestMethod]
    public void DesktopIniIsNeverAnApp()
        => Assert.IsFalse(StartMenuShortcutResolver.ShouldIndex(@"C:\Apps\desktop.ini"));
}
