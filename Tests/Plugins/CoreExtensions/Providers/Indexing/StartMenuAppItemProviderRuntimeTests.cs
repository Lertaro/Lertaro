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
}
