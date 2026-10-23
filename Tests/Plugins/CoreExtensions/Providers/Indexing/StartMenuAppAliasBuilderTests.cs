using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
public sealed class StartMenuAppAliasBuilderTests
{
    [TestMethod]
    public void FromShortcut_CachesExecutableStemAndTrustedVersionMetadataWithProvenance()
    {
        var aliases = StartMenuAppAliasBuilder.FromShortcut(
            "截图工具",
            @"C:\Program Files\PixPin\PixPin.exe",
            new StartMenuAppAliasBuilder.FileMetadata(
                "PixPin 2",
                "Screen Capture",
                "PixPin.exe"));

        CollectionAssert.AreEqual(
            new[] { "PixPin", "PixPin 2", "Screen Capture" },
            aliases.Select(alias => alias.Text).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                SearchableItemAliasKind.ShortcutTarget,
                SearchableItemAliasKind.ProductMetadata,
                SearchableItemAliasKind.ProductMetadata,
            },
            aliases.Select(alias => alias.Kind).ToArray());
    }

    [TestMethod]
    public void FromShortcut_DoesNotPromoteNonExecutableTargetNames()
    {
        var aliases = StartMenuAppAliasBuilder.FromShortcut(
            "Helper",
            @"C:\Program Files\Suite\payload.dll",
            default);

        Assert.IsEmpty(aliases);
    }

    [TestMethod]
    public void FromPackage_UsesStablePackageNamesButRejectsFilePaths()
    {
        var aliases = StartMenuAppAliasBuilder.FromPackage(
            "Calculator",
            "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        CollectionAssert.AreEqual(
            new[] { "Microsoft.WindowsCalculator", "WindowsCalculator" },
            aliases.Select(alias => alias.Text).ToArray());
        Assert.IsTrue(aliases.All(alias => alias.Kind == SearchableItemAliasKind.ProductMetadata));
        Assert.IsEmpty(StartMenuAppAliasBuilder.FromPackage("PixPin", @"C:\Apps\PixPin.exe"));
    }
}
