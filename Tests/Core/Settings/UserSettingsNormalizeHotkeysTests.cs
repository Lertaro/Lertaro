namespace Lertaro.Core.Tests.Settings;

// NormalizeHotkeys is a pure in-memory mutation (no file I/O), so unlike Load()/Save()/ForceReload()
// (see UserSettingsPluginSettingTests' comment) it's safe to exercise directly on a fresh instance.
[TestClass]
public sealed class UserSettingsNormalizeHotkeysTests
{
    [TestMethod]
    public void NormalizeHotkeys_EmptyToggleWindowHotkey_FallsBackToDefault()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "";

        UserSettings.NormalizeHotkeys(settings);

        Assert.AreEqual(new HotkeyPageSettings().ToggleWindowHotkey, settings.Hotkeys.ToggleWindowHotkey);
    }

    [TestMethod]
    public void NormalizeHotkeys_WhitespaceToggleWindowHotkey_FallsBackToDefault()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "   ";

        UserSettings.NormalizeHotkeys(settings);

        Assert.AreEqual(new HotkeyPageSettings().ToggleWindowHotkey, settings.Hotkeys.ToggleWindowHotkey);
    }

    [TestMethod]
    public void NormalizeHotkeys_NonEmptyToggleWindowHotkey_IsLeftUnchanged()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";

        UserSettings.NormalizeHotkeys(settings);

        Assert.AreEqual("Alt+Space", settings.Hotkeys.ToggleWindowHotkey);
    }
}
