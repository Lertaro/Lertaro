namespace Lertaro.Core.Tests.Settings;

// Only covers UserSettings' pure in-memory Get/SetPluginSetting logic on a fresh instance -- Load(),
// Save(), and ForceReload() are deliberately NOT tested here: SettingsPath is derived from the
// non-injectable Logger.UserDataDir (a readonly static pointing at the real %LocalAppData%\Lertaro),
// so exercising them would read/overwrite this machine's actual Lertaro settings file.
[TestClass]
public sealed class UserSettingsPluginSettingTests
{
    [TestMethod]
    public void GetPluginSetting_NotSet_ReturnsDefaultValue()
    {
        var settings = new UserSettings();

        var value = settings.GetPluginSetting("myplugin", "key", "fallback");

        Assert.AreEqual("fallback", value);
    }

    [TestMethod]
    public void SetPluginSetting_ThenGetPluginSetting_RoundTripsStringValue()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("myplugin", "key", "hello");

        var value = settings.GetPluginSetting("myplugin", "key", "fallback");

        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public void SetPluginSetting_ThenGetPluginSetting_RoundTripsIntValue()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("myplugin", "count", 42);

        var value = settings.GetPluginSetting("myplugin", "count", 0);

        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void SetPluginSetting_ThenGetPluginSetting_RoundTripsBoolValue()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("myplugin", "enabled", true);

        var value = settings.GetPluginSetting("myplugin", "enabled", false);

        Assert.IsTrue(value);
    }

    [TestMethod]
    public void SetPluginSetting_NullValue_RemovesTheKey()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("myplugin", "key", "hello");
        settings.SetPluginSetting("myplugin", "key", null);

        var value = settings.GetPluginSetting("myplugin", "key", "fallback");

        Assert.AreEqual("fallback", value);
    }

    [TestMethod]
    public void SetPluginSetting_DifferentPlugins_DoNotShareKeys()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("pluginA", "key", "a-value");
        settings.SetPluginSetting("pluginB", "key", "b-value");

        Assert.AreEqual("a-value", settings.GetPluginSetting("pluginA", "key", "fallback"));
        Assert.AreEqual("b-value", settings.GetPluginSetting("pluginB", "key", "fallback"));
    }

    [TestMethod]
    public void GetPluginSetting_PluginIdLookup_IsCaseInsensitive()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("MyPlugin", "key", "hello");

        var value = settings.GetPluginSetting("myplugin", "key", "fallback");

        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public void GetPluginSetting_WrongTypeRequested_FallsBackToDefaultInsteadOfThrowing()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("myplugin", "key", "not-a-number");

        var value = settings.GetPluginSetting("myplugin", "key", -1);

        Assert.AreEqual(-1, value);
    }
}
