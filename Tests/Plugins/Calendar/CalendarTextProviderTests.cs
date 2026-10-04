using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.Tests;

/// <summary>
/// Covers <see cref="CalendarTextProvider"/>, the one-line 农历 answer the host's search-box clock shows.
/// </summary>
/// <remarks>
/// The provider reads the interface language through the same PluginSdk seam every other translated string
/// does, and its own switch through the plugin-settings seam, so these tests pin both (and restore them
/// afterwards) rather than depending on the machine's locale or on a real settings file.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class CalendarTextProviderTests
{
    private static readonly CalendarTextProvider Provider = new();
    private Func<string> _originalCultureFunc = null!;
    private Func<string, string, object?, object?>? _originalGetSettingFunc;

    [TestInitialize]
    public void SaveSeams()
    {
        _originalCultureFunc = TranslationService.CurrentCultureFunc;
        _originalGetSettingFunc = PluginSettingsService.GetSettingFunc;
        // A clean slate, so every test starts from "nothing has ever been persisted" (the switch's default)
        // rather than from whatever another test class happened to leave wired up.
        PluginSettingsService.GetSettingFunc = null;
    }

    [TestCleanup]
    public void RestoreSeams()
    {
        TranslationService.CurrentCultureFunc = _originalCultureFunc;
        TranslationService.NotifyCultureChanged(_originalCultureFunc());
        PluginSettingsService.GetSettingFunc = _originalGetSettingFunc;
    }

    private static void UseChinese() => TranslationService.CurrentCultureFunc = () => "zh-CN";

    /// <summary>Answers the plugin's own clock switch with <paramref name="enabled"/>, everything else default.</summary>
    private static void UseClockSwitch(bool enabled) =>
        PluginSettingsService.GetSettingFunc = (_, key, fallback) =>
            key == CalendarPlugin.LunarInClockKey ? enabled : fallback;

    [TestMethod]
    [DataRow(2026, 10, 4, "八月廿四")]
    [DataRow(2026, 10, 1, "八月廿一")]
    [DataRow(2026, 9, 7, "七月白露")]
    [DataRow(2026, 9, 23, "八月秋分")]
    [DataRow(2026, 9, 25, "八月中秋节")]
    [DataRow(2026, 9, 11, "八月初一")]
    [DataRow(2026, 2, 17, "正月春节")]
    public void GetCalendarText_ReadsMonthThenDayTermOrFestival(int year, int month, int day, string expected)
    {
        UseChinese();

        Assert.AreEqual(expected, Provider.GetCalendarText(new DateTime(year, month, day)));
    }

    [TestMethod]
    public void GetCalendarText_ShowsNothingForALanguageWithoutAChineseCalendarLayer()
    {
        // 农历, 节气 and the festival names are Chinese characters with no translation behind them, so the
        // other interface languages get an empty answer and the host leaves its clock line alone.
        TranslationService.CurrentCultureFunc = () => "en-US";

        Assert.AreEqual(string.Empty, Provider.GetCalendarText(new DateTime(2026, 9, 25)));
    }

    [TestMethod]
    public void GetCalendarText_ShowsNothingWhileThePluginsOwnSwitchIsOff()
    {
        // The switch lives in this plugin's settings, not the host's, so this is the whole of what a user
        // turning the clock's 农历 off has to produce.
        UseChinese();
        UseClockSwitch(enabled: false);

        Assert.AreEqual(string.Empty, Provider.GetCalendarText(new DateTime(2026, 9, 25)));
    }

    [TestMethod]
    public void GetCalendarText_IsOnWhenNothingHasEverBeenPersisted()
    {
        // The default is on, and a never-configured plugin has no stored value: the provider has to read the
        // same default its schema declares, or the line would be blank until someone visited the settings page.
        UseChinese();
        PluginSettingsService.GetSettingFunc = null;

        Assert.AreEqual("八月中秋节", Provider.GetCalendarText(new DateTime(2026, 9, 25)));
    }

    [TestMethod]
    public void GetCalendarText_ConvertsToTheRegionsScript()
    {
        // 中秋节/春节 come out of the library's Simplified tables and zh-HK writes them the other way, which
        // is the same conversion the month grid's own sublabels go through.
        TranslationService.CurrentCultureFunc = () => "zh-HK";
        var hongKong = Provider.GetCalendarText(new DateTime(2026, 9, 25));

        TranslationService.CurrentCultureFunc = () => "zh-CN";
        var mainland = Provider.GetCalendarText(new DateTime(2026, 9, 25));

        Assert.AreEqual("八月中秋節", hongKong);
        Assert.AreNotEqual(mainland, hongKong);
    }

    [TestMethod]
    public void LunarMonthText_NamesTheMonthRegardlessOfTheDay()
    {
        Assert.AreEqual("八月", ChineseCalendar.LunarMonthText(new DateTime(2026, 9, 11)));
        Assert.AreEqual("八月", ChineseCalendar.LunarMonthText(new DateTime(2026, 9, 25)));
        Assert.AreEqual("正月", ChineseCalendar.LunarMonthText(new DateTime(2026, 2, 17)));
    }
}
