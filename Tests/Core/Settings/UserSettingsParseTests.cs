using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

// TryParse is a pure parse-and-normalize function (no file I/O, no static state writes apart from
// logging), so it's safe to exercise directly on string fixtures.
[TestClass]
public sealed class UserSettingsParseTests
{
    [TestMethod]
    public void TryParse_NullPluginDisableList_RejectsCorruptStateInsteadOfLoadingPlugins()
        => Assert.IsNull(UserSettings.TryParse("""{"DisabledPluginAssemblies":null}"""));

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
    public void TryParse_CleansTheUiFontFallbackChain()
    {
        var json = JsonSerializer.Serialize(new UserSettings
        {
            UiFontFamilies = [" Microsoft YaHei ", "microsoft yahei", "", "Bad,Name", "微软雅黑", "Tahoma", "Extra"]
        });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        CollectionAssert.AreEqual(new[] { "Microsoft YaHei", "微软雅黑", "Tahoma" }, settings.UiFontFamilies);
    }

    [TestMethod]
    public void TryParse_BlankToggleWindowHotkey_FallsBackToDefault()
    {
        var json = JsonSerializer.Serialize(new UserSettings { Hotkeys = new HotkeyPageSettings { ToggleWindowHotkey = "" } });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(new HotkeyPageSettings().ToggleWindowHotkey, settings.Hotkeys.ToggleWindowHotkey);
    }

    [TestMethod]
    public void TryParse_LegacySharedDisplay_SeedsBothWindowsAndRetiresOnlyTheLegacyField()
    {
        var json = JsonSerializer.Serialize(new { DefaultSearchDisplay = @"\\.\DISPLAY2", FuturePreference = "keep" });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(@"\\.\DISPLAY2", settings.SearchWindow.DefaultDisplay);
        Assert.AreEqual(@"\\.\DISPLAY2", settings.MainWindow.DefaultDisplay);
        using var saved = JsonDocument.Parse(JsonSerializer.Serialize(settings));
        Assert.IsFalse(saved.RootElement.TryGetProperty("DefaultSearchDisplay", out _));
        Assert.AreEqual("keep", saved.RootElement.GetProperty("FuturePreference").GetString());
    }

    [TestMethod]
    [DataRow("", null, "", @"\\.\DISPLAY2")]
    [DataRow(@"\\.\DISPLAY1", null, @"\\.\DISPLAY1", @"\\.\DISPLAY2")]
    [DataRow(null, "", @"\\.\DISPLAY2", "")]
    [DataRow(null, @"\\.\DISPLAY1", @"\\.\DISPLAY2", @"\\.\DISPLAY1")]
    public void TryParse_PerWindowDisplay_OverridesTheLegacyValueIncludingFollowingTheMouse(
        string? quickDisplay, string? fullDisplay, string expectedQuick, string expectedFull)
    {
        var json = JsonSerializer.Serialize(new
        {
            DefaultSearchDisplay = @"\\.\DISPLAY2",
            SearchWindow = new { DefaultDisplay = quickDisplay },
            MainWindow = new { DefaultDisplay = fullDisplay }
        });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(expectedQuick, settings.SearchWindow.DefaultDisplay);
        Assert.AreEqual(expectedFull, settings.MainWindow.DefaultDisplay);
    }

    [TestMethod]
    public void SettingsRoundTrip_PreservesIndependentWindowDisplays()
    {
        var settings = new UserSettings();
        settings.SearchWindow.DefaultDisplay = @"\\.\DISPLAY1";
        settings.MainWindow.DefaultDisplay = @"\\.\DISPLAY2";

        var restored = UserSettings.TryParse(JsonSerializer.Serialize(settings));

        Assert.IsNotNull(restored);
        Assert.AreEqual(@"\\.\DISPLAY1", restored.SearchWindow.DefaultDisplay);
        Assert.AreEqual(@"\\.\DISPLAY2", restored.MainWindow.DefaultDisplay);
    }

    [TestMethod]
    public void TryParse_NoDisplayPreference_BothWindowsFollowTheMouse()
    {
        var settings = UserSettings.TryParse("{}");

        Assert.IsNotNull(settings);
        Assert.AreEqual(string.Empty, settings.SearchWindow.DefaultDisplay);
        Assert.AreEqual(string.Empty, settings.MainWindow.DefaultDisplay);
    }

    [TestMethod]
    public void SettingsRoundTrip_PreservesWholePluginAndComponentStatesIndependently()
    {
        var settings = new UserSettings
        {
            DisabledPluginAssemblies = ["Lertaro.Plugins.A.dll"],
            DisabledPluginComponents = ["Lertaro.Plugins.A.dll::Action::One"]
        };
        var restored = UserSettings.TryParse(JsonSerializer.Serialize(settings));
        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(settings.DisabledPluginAssemblies, restored.DisabledPluginAssemblies);
        CollectionAssert.AreEqual(settings.DisabledPluginComponents, restored.DisabledPluginComponents);
        Assert.IsEmpty(UserSettings.TryParse("{}")!.DisabledPluginAssemblies);
    }
}
