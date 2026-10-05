using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

// TryParse is a pure parse-and-normalize function (no file I/O, no static state writes apart from
// logging), so it's safe to exercise directly on string fixtures.
[TestClass]
public sealed class UserSettingsParseTests
{
    [TestMethod]
    public void TryParse_ValidJson_ReturnsSettings()
    {
        var json = JsonSerializer.Serialize(new UserSettings { LogLevel = "Debug" });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual("Debug", settings.LogLevel);
    }

    [TestMethod]
    public void TryParse_TruncatedJson_ReturnsNull() => Assert.IsNull(UserSettings.TryParse("{ truncated"));

    [TestMethod]
    public void TryParse_BlankToggleWindowHotkey_FallsBackToDefault()
    {
        var json = JsonSerializer.Serialize(new UserSettings { Hotkeys = new HotkeyPageSettings { ToggleWindowHotkey = "" } });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(new HotkeyPageSettings().ToggleWindowHotkey, settings.Hotkeys.ToggleWindowHotkey);
    }

    [TestMethod]
    public void TryParse_SettingsItemAliases_SurvivesRoundTrip()
    {
        var json = JsonSerializer.Serialize(new UserSettings
        {
            SettingsItemAliases = { ["env"] = ["编辑系统环境变量", "编辑账户的环境变量"], ["log"] = ["事件查看器"] },
        });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.HasCount(2, settings.SettingsItemAliases["ENV"]);
        Assert.Contains("编辑系统环境变量", settings.SettingsItemAliases["env"]);
        Assert.Contains("编辑账户的环境变量", settings.SettingsItemAliases["env"]);
    }

    [TestMethod]
    public void TryParse_LegacyAliasAndNewAliasArrays_MergesCaseInsensitiveTargets()
    {
        var settings = UserSettings.TryParse("""
            {"SettingsItemAliases":{"env":"编辑系统环境变量","ENV":["编辑账户的环境变量","编辑系统环境变量"]}}
            """);
        Assert.IsNotNull(settings);
        Assert.HasCount(1, settings.SettingsItemAliases);
        Assert.HasCount(2, settings.SettingsItemAliases["env"]);
    }

    [TestMethod]
    public void TryParse_NullAliasTable_UsesEmptyTable()
    {
        var settings = UserSettings.TryParse("""{"SettingsItemAliases":null}""");
        Assert.IsNotNull(settings);
        Assert.IsEmpty(settings.SettingsItemAliases);
    }
}
