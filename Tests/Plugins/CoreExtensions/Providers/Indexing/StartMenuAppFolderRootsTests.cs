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

    // ---------------------------------------------------------------------------------------------
    // The custom-folder list's default
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void ResolveCustomFolders_Unset_TakesTheShippedDefault() =>
        CollectionAssert.AreEqual(
            new[] { "shell:appsfolder" },
            StartMenuAppFolderRoots.ResolveCustomFolders(null).ToList());

    [TestMethod]
    public void ResolveCustomFolders_Empty_TakesTheShippedDefault() =>
        CollectionAssert.AreEqual(
            new[] { "shell:appsfolder" },
            StartMenuAppFolderRoots.ResolveCustomFolders([]).ToList());

    // The field is one path per line, so a blank line the user left behind is still "nothing configured".
    [TestMethod]
    public void ResolveCustomFolders_OnlyBlankEntries_TakesTheShippedDefault() =>
        CollectionAssert.AreEqual(
            new[] { "shell:appsfolder" },
            StartMenuAppFolderRoots.ResolveCustomFolders(["", "   "]).ToList());

    // Anything real replaces the default outright: nothing is appended to a list the user wrote.
    [TestMethod]
    public void ResolveCustomFolders_Configured_IsUsedExactlyAsGiven() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\Apps" },
            StartMenuAppFolderRoots.ResolveCustomFolders([@"D:\Apps"]).ToList());

    // A user who keeps the shipped value alongside their own folder must not end up with it twice.
    [TestMethod]
    public void ResolveCustomFolders_ConfiguredIncludingTheDefault_IsNotDuplicated() =>
        CollectionAssert.AreEqual(
            new[] { "shell:appsfolder", @"D:\Apps" },
            StartMenuAppFolderRoots.ResolveCustomFolders(["shell:appsfolder", @"D:\Apps"]).ToList());

    // The schema's default and the resolver's fallback are one value, not two spellings of one idea.
    [TestMethod]
    public void ConfigSchema_CustomFoldersDefaultMatchesTheResolverFallback()
    {
        var field = new CoreExtensionsPlugin().GetConfigSchema().Fields
            .Single(f => f.Key == "CustomFoldersGroup").SubFields!
            .Single(f => f.Key == "CustomFolders");

        CollectionAssert.AreEqual(
            StartMenuAppFolderRoots.DefaultCustomFolders.ToList(),
            ((List<string>)field.DefaultValue!).ToList());
    }
}
