using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// The plugin list leads with what the user can act on: plugins with settings, then plugins with
// switches, then the ones offering neither. BuildPluginList needs a live plugin directory, so what is
// pinned here is the ranking rule it sorts by, which is the part that carries the decision.
[TestClass]
public sealed class PluginDisplayRankTests
{
    [TestMethod]
    public void ConfigurablePluginsRankAheadOfSwitchableOnes() => Assert.IsLessThan(
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: true),
            PluginLoaderHelper.DisplayRank(hasConfigFields: true, hasAnyToggleableComponent: false));

    [TestMethod]
    public void SwitchablePluginsRankAheadOfInertOnes() => Assert.IsLessThan(
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: false),
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: true));

    [TestMethod]
    public void ConfigWinsWhetherOrNotThereAreAlsoSwitches() =>
        // Config is the stronger signal, so having switches as well must not move a plugin anywhere.
        Assert.AreEqual(
            PluginLoaderHelper.DisplayRank(hasConfigFields: true, hasAnyToggleableComponent: false),
            PluginLoaderHelper.DisplayRank(hasConfigFields: true, hasAnyToggleableComponent: true));

    [TestMethod]
    public void ASingleToggleableComponentStillCountsAsSwitchable()
    {
        // The trap this rule was written around: PluginInfoViewModel.HasToggleableComponents means
        // "more than one" because it gates the Select All link, so reusing it would have filed a plugin
        // with exactly one switch under "nothing to do here". The rank takes "any", and one is any.
        Assert.AreEqual(
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: true),
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: true));

        Assert.AreNotEqual(
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: false),
            PluginLoaderHelper.DisplayRank(hasConfigFields: false, hasAnyToggleableComponent: true));
    }
}
