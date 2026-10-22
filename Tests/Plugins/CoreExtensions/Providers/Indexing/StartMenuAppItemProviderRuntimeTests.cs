using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
[DoNotParallelize]
public sealed class StartMenuAppItemProviderRuntimeTests
{
    [TestInitialize]
    public void ResetBefore() => PluginSettingsService.IsComponentEnabledFunc = null;

    [TestCleanup]
    public void ResetAfter() => PluginSettingsService.IsComponentEnabledFunc = null;

    [TestMethod]
    public void GetSearchableItems_DisabledComponent_ReturnsEmptyWithoutRegisteringRuntime()
    {
        PluginSettingsService.IsComponentEnabledFunc = (_, _, _) => false;

        using var provider = new StartMenuAppItemProvider();

        Assert.IsEmpty(provider.GetSearchableItems());
    }

    // The host finds the "application" type by this declaration alone (a .lnk index row borrows that
    // type's rank for the quick window's result-type order), and the literal is a cross-assembly contract:
    // the App side cannot reference this type, so nothing else would catch a rename. Read it through the
    // interface -- that is the only path the host has, and a provider compiled against an older SDK would
    // still expose the property on the concrete type while returning null here.
    [TestMethod]
    public void ItemResultKind_IsTheApplicationKindTheHostLooksFor()
    {
        using var provider = new StartMenuAppItemProvider();

        Assert.AreEqual("Application", ((ISearchableItemProvider)provider).ItemResultKind);
    }
}
