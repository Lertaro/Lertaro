using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class PluginSettingsStoreTests
{
    private readonly string _root = Directory.CreateTempSubdirectory("LertaroParameters-").FullName;
    private string Main => Path.Combine(_root, "user-settings.json");

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in Directory.GetFiles(_root)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    [TestMethod]
    public void Save_UnchangedParameters_DoesNotRotateBackupOrRewriteHost()
    {
        File.WriteAllText(Main, "host settings");
        Dictionary<string, Dictionary<string, object>> values = new() { ["plugin"] = new() { ["key"] = "value" } };
        PluginSettingsStore.Save(Main, values);
        PluginSettingsStore.Save(Main, values);
        Assert.IsFalse(File.Exists(PluginSettingsStore.PathFor(Main) + ".bak.1"));
        Assert.AreEqual("host settings", File.ReadAllText(Main));
    }

    [TestMethod]
    public void Load_CorruptPrimary_UsesNewestValidBackup()
    {
        var path = PluginSettingsStore.PathFor(Main);
        File.WriteAllText(path, "bad");
        File.WriteAllText(path + ".bak.1", "null");
        File.WriteAllText(path + ".bak.2", """{"Plugin":{"Key":"retained"}}""");
        Assert.AreEqual("retained", PluginSettingsStore.Load(Main)["plugin"]["key"].ToString());
    }

    [TestMethod]
    public void Save_ReadOnlyDestination_PreservesParameters()
    {
        var path = PluginSettingsStore.PathFor(Main);
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => PluginSettingsStore.Save(Main, new() { ["P"] = new() { ["K"] = 1 } }));
        Assert.AreEqual("{}", File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{\"P\":null}")]
    public void Parse_NullObjects_RejectsInvalidShape(string json) =>
        Assert.ThrowsExactly<InvalidDataException>(() => PluginSettingsStore.Parse(json));

    [TestMethod]
    public void Parse_Array_RejectsInvalidShape() =>
        Assert.ThrowsExactly<JsonException>(() => PluginSettingsStore.Parse("[]"));
}
