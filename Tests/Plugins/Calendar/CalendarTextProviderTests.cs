using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.Tests;

/// <summary>
/// Covers <see cref="CalendarTextProvider"/>, the one-line 农历 answer the host's search-box clock shows.
/// </summary>
/// <remarks>
/// The provider reads the interface language through the same PluginSdk seam every other translated string
/// does, so these tests pin it (and restore it afterwards) rather than depending on the machine's locale.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class CalendarTextProviderTests
{
    private static readonly CalendarTextProvider Provider = new();
    private Func<string> _originalCultureFunc = null!;

    [TestInitialize]
    public void SaveSeam() => _originalCultureFunc = TranslationService.CurrentCultureFunc;

    [TestCleanup]
    public void RestoreSeam()
    {
        TranslationService.CurrentCultureFunc = _originalCultureFunc;
        TranslationService.NotifyCultureChanged(_originalCultureFunc());
    }

    private static void UseChinese() => TranslationService.CurrentCultureFunc = () => "zh-CN";

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
