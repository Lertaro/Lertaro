using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// Opening a plugin's configuration must not mark untouched settings for persistence.
[TestClass]
public sealed class PluginConfigCommitSupportTests
{
    [TestMethod]
    public void HasPendingConfigEdits_TracksOnlyActualEdits()
    {
        var plugin = MakePlugin();
        Assert.IsFalse(plugin.HasPendingConfigEdits, "a freshly built plugin has nothing staged");

        // Opening the config tab alone is not an edit -- otherwise merely looking at a plugin would
        // commit it (and run its OnSave hook) on the next Apply.
        plugin.IsConfigTab = true;
        Assert.IsFalse(plugin.HasPendingConfigEdits, "opening a config is not editing it");

        plugin.ConfigFields[0].Value = "typed";
        Assert.IsTrue(plugin.HasPendingConfigEdits);
    }

    private static PluginInfoViewModel MakePlugin()
    {
        var field = new PluginConfigFieldViewModel(
            "p",
            new PluginSdk.Abstractions.PluginConfigField { Key = "k", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" },
            new UserSettings());
        return new PluginInfoViewModel("P", "1.0", "P.dll", "1.0-sdk", [], [field]);
    }
}
