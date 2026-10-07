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
    public void TryParse_BlankToggleWindowHotkey_FallsBackToDefault()
    {
        var json = JsonSerializer.Serialize(new UserSettings { Hotkeys = new HotkeyPageSettings { ToggleWindowHotkey = "" } });

        var settings = UserSettings.TryParse(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(new HotkeyPageSettings().ToggleWindowHotkey, settings.Hotkeys.ToggleWindowHotkey);
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
