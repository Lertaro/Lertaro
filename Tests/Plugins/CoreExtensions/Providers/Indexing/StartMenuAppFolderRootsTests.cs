using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
public sealed class StartMenuAppFolderRootsTests
{
    [TestMethod]
    public void Merge_IncludesExistingCustomRootsAlongsideBuiltInRoots()
    {
        var roots = StartMenuAppFolderRoots.Merge(
            [@"C:\StartMenu"],
            [@"Z:\Apps", @"Z:\Missing"],
            path => path is @"C:\StartMenu" or @"Z:\Apps");

        CollectionAssert.AreEquivalent(new[] { @"C:\StartMenu", @"Z:\Apps" }, roots.ToList());
    }

    [TestMethod]
    public void Merge_ResolvesVirtualShellPathsBeforeCheckingExistence()
    {
        var resolvedStartup = @"C:\Users\testuser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup";

        var roots = StartMenuAppFolderRoots.Merge(
            [],
            ["shell:startup", @"C:\Real"],
            path => path is @"C:\Users\testuser\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup" or @"C:\Real",
            path => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? resolvedStartup : path);

        CollectionAssert.AreEquivalent(
            new[] { resolvedStartup, @"C:\Real" },
            roots.ToList());
    }

    [TestMethod]
    public void Merge_DeduplicatesRepeatedRootsAndDropsBlankOrMissingEntries()
    {
        var roots = StartMenuAppFolderRoots.Merge(
            [@"C:\StartMenu", @"c:\startmenu"],
            [" ", @"C:\StartMenu", @"D:\Missing"],
            path => path.Equals(@"C:\StartMenu", StringComparison.OrdinalIgnoreCase));

        CollectionAssert.AreEquivalent(new[] { @"C:\StartMenu" }, roots.ToList());
    }
}
