using System.Text.Json;
using Lertaro.Core.Settings.Migration;

namespace Lertaro.Core.Tests.Settings.Migration;

[TestClass]
public sealed class LegacyPluginSettingsMigrationTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroMigration-").FullName;
    private string Main => Path.Combine(_root, "user-settings.json");
    private const string Legacy = """{"Future":{"flag":true},"PluginSettings":{"Disabled.Plugin":{"secret":"old"}}}""";

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public void Upgrade_MigratesOncePreservingUnknownFieldsAndDisabledPluginParameters()
    {
        File.WriteAllText(Main, Legacy);
        LegacyPluginSettingsMigration.Upgrade(Main, UserSettings.TryParse(Legacy)!);
        using var result = JsonDocument.Parse(File.ReadAllText(Main));
        Assert.IsFalse(result.RootElement.TryGetProperty("PluginSettings", out _));
        Assert.IsTrue(result.RootElement.GetProperty("Future").GetProperty("flag").GetBoolean());
        Assert.AreEqual(1, result.RootElement.GetProperty("PluginSettingsStorageVersion").GetInt32());
        Assert.AreEqual("old", PluginSettingsStore.Load(Main)["disabled.plugin"]["SECRET"].ToString());
        File.Delete(PluginSettingsStore.PathFor(Main));
        LegacyPluginSettingsMigration.Upgrade(Main, UserSettings.TryParse(File.ReadAllText(Main))!);
        Assert.ThrowsExactly<InvalidDataException>(() => PluginSettingsStore.Load(Main));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(".bak.1")]
    public void Upgrade_InterruptedMigration_PreservesNewerParameters(string suffix)
    {
        File.WriteAllText(Main, Legacy);
        File.WriteAllText(PluginSettingsStore.PathFor(Main) + suffix, """{"Disabled.Plugin":{"secret":"new"}}""");
        LegacyPluginSettingsMigration.Upgrade(Main, UserSettings.TryParse(Legacy)!);
        Assert.AreEqual("new", PluginSettingsStore.Load(Main)["Disabled.Plugin"]["secret"].ToString());
    }

    [TestMethod]
    public void Upgrade_CorruptNewFile_DoesNotOverwriteWithLegacyData()
    {
        File.WriteAllText(Main, Legacy);
        File.WriteAllText(PluginSettingsStore.PathFor(Main), "broken");
        Assert.ThrowsExactly<InvalidDataException>(() => LegacyPluginSettingsMigration.Upgrade(Main, UserSettings.TryParse(Legacy)!));
        Assert.AreEqual(Legacy, File.ReadAllText(Main));
        Assert.AreEqual("broken", File.ReadAllText(PluginSettingsStore.PathFor(Main)));
    }
}
