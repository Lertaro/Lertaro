using System.IO;
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

    [TestMethod]
    public void CommitPending_OpenedCustomPanel_SavesEvenWithoutOrdinaryFieldEdits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"custom-panel-{Guid.NewGuid():N}.json");
        var dictionaryPath = "";
        var saves = 0;
        var field = new PluginConfigFieldViewModel("flow", new PluginSdk.Abstractions.PluginConfigField
        {
            Key = "MDict", FieldType = PluginSdk.Abstractions.ConfigFieldType.Group,
            SubFields = [new PluginSdk.Abstractions.PluginConfigField
            {
                Key = "MDict.CustomPanel", FieldType = PluginSdk.Abstractions.ConfigFieldType.CustomControl
            }]
        }, new UserSettings());
        var plugin = new PluginInfoViewModel("Flow", "1", "Flow.dll", "1", [], [field], onSave: () =>
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { DictPath = dictionaryPath }));
            saves++;
        });
        try
        {
            PluginConfigCommitSupport.CommitPending([plugin]);
            Assert.AreEqual(0, saves, "A panel never opened should not invoke its save hook.");
            plugin.IsConfigTab = true;
            dictionaryPath = @"D:\Dictionaries\test.mdx"; // Custom UI edits its own model directly.
            plugin.IsConfigTab = false; // Switching plugins before Apply must preserve this save.
            Assert.IsFalse(plugin.HasPendingConfigEdits);

            PluginConfigCommitSupport.CommitPending([plugin]);

            Assert.AreEqual(1, saves);
            using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(dictionaryPath, saved.RootElement.GetProperty("DictPath").GetString());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void CommitPending_OpenedOrdinaryFieldsWithoutEdits_DoesNotInvokeSave()
    {
        var saves = 0;
        var plugin = new PluginInfoViewModel("P", "1", "P.dll", "1", [],
            MakePlugin().ConfigFields.ToList(), onSave: () => saves++);
        plugin.IsConfigTab = true;
        PluginConfigCommitSupport.CommitPending([plugin]);
        Assert.AreEqual(0, saves);
    }
}
