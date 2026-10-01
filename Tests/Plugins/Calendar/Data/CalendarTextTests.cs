using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.Tests.Data;

[TestClass]
[DoNotParallelize]
public sealed class CalendarTextTests
{
    private Func<string> _originalCultureFunc = null!;

    [TestInitialize]
    public void SaveSeam() => _originalCultureFunc = TranslationService.CurrentCultureFunc;

    [TestCleanup]
    public void RestoreSeam()
    {
        TranslationService.CurrentCultureFunc = _originalCultureFunc;
        TranslationService.NotifyCultureChanged(_originalCultureFunc());
    }

    [TestMethod]
    public void Culture_FollowsTheAppLanguageRatherThanTheOperatingSystem()
    {
        // The whole reason this exists: a user running a Japanese interface on an English Windows has to
        // see Japanese weekday names, so the culture is taken from the app's own setting.
        TranslationService.CurrentCultureFunc = () => "ja-JP";

        Assert.AreEqual("ja-JP", CalendarText.Culture().Name);
        Assert.AreEqual("水", CalendarText.Culture().DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Wednesday));

        TranslationService.CurrentCultureFunc = () => "en-US";
        Assert.AreEqual("Wed", CalendarText.Culture().DateTimeFormat.GetAbbreviatedDayName(DayOfWeek.Wednesday));
    }

    [TestMethod]
    public void Culture_ResolvesAnEmptyNameToTheInvariantCulture()
    {
        TranslationService.CurrentCultureFunc = () => string.Empty;

        Assert.AreEqual(string.Empty, CalendarText.Culture().Name);
    }

    [TestMethod]
    public void Culture_ReleasesItsCacheWhenTheLanguageChanges()
    {
        TranslationService.CurrentCultureFunc = () => "ko-KR";
        Assert.AreEqual("ko-KR", CalendarText.Culture().Name);

        // The app raises this on every language switch; a cache that never let go would leave the grid
        // showing the previous language's day names.
        TranslationService.CurrentCultureFunc = () => "es-ES";
        TranslationService.NotifyCultureChanged("es-ES");
        Assert.AreEqual("es-ES", CalendarText.Culture().Name);
    }

    [TestMethod]
    public void Labels_AreFormattedForTheCurrentLanguage()
    {
        TranslationService.CurrentCultureFunc = () => "zh-CN";
        var at = new DateTime(2026, 9, 30, 14, 5, 0);

        Assert.AreEqual("2026年9月", CalendarText.MonthTitle(2026, 9));
        Assert.AreEqual("14:05", CalendarText.Time(at));
        Assert.AreEqual("2026/9/30", CalendarText.Date(at));
    }

    [TestMethod]
    public void WeekdayOrder_CoversEveryDayOnceForEitherStart()
    {
        foreach (var first in new[] { DayOfWeek.Monday, DayOfWeek.Sunday })
        {
            var order = ChineseCalendar.WeekdayOrder(first);
            Assert.AreEqual(7, order.Count);
            Assert.AreEqual(7, order.Distinct().Count());
            Assert.AreEqual(first, order[0]);
        }
    }
}
