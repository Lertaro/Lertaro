using System.Globalization;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Calendar.Data;

/// <summary>
/// Strings the locale already owns, kept out of the translation files on purpose.
/// </summary>
/// <remarks>
/// Weekday headers, the month title and a time are what <see cref="CultureInfo"/> is for: it gets
/// 周一 / Mon / 월 / Lun right in a way a hand-maintained table of 19 keys would not, and it keeps them in
/// step with the app's own language instead of the OS locale. The culture therefore comes from
/// <see cref="TranslationService.GetCurrentCulture"/> and never from <see cref="CultureInfo.CurrentUICulture"/>,
/// so a user who runs the launcher in Japanese on an English Windows still sees Japanese weekdays.
/// </remarks>
internal static class CalendarText
{
    private static readonly object Gate = new();
    private static string? _cachedName;
    private static CultureInfo? _cached;

    static CalendarText() => TranslationService.CultureChanged += _ =>
                                  {
                                      lock (Gate)
                                      {
                                          _cachedName = null;
                                          _cached = null;
                                      }
                                  };

    internal static CultureInfo Culture()
    {
        lock (Gate)
        {
            var name = TranslationService.GetCurrentCulture();
            // GetCultureInfo is cached by the runtime and returns the invariant culture for an empty
            // name, so there is nothing to guard here beyond not calling it again per label.
            if (!string.Equals(name, _cachedName, StringComparison.OrdinalIgnoreCase))
            {
                _cachedName = name;
                _cached = CultureInfo.GetCultureInfo(name);
            }
            return _cached!;
        }
    }

    /// <summary>
    /// Whether the Chinese-calendar layer is shown at all: only the three Chinese interface languages.
    /// </summary>
    /// <remarks>
    /// 农历, 节气, 调休 and the 黄历 reading are Chinese characters with no translation behind them, so in
    /// the other languages they read as untranslated strings rather than as content. Checked on the two-letter
    /// name so it covers zh-CN/zh-HK/zh-TW and any future zh-* without a list to maintain. The invariant
    /// culture (an empty language name) is not zh, so a broken setting hides the layer rather than leaking it.
    /// </remarks>
    internal static bool ShowsChineseCalendar =>
        string.Equals(Culture().TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the Chinese-calendar layer has to be put into Traditional characters. The library's tables
    /// are Simplified, so this is about the two regions whose interface language is written the other way
    /// and not about zh-CN; keyed on the region name because that is what the app's seven locales use, and
    /// a zh-* locale nobody has added a script hint to still gets the Simplified it asks for.
    /// </summary>
    internal static bool UsesTraditionalScript =>
        ShowsChineseCalendar && Culture().Name is "zh-HK" or "zh-TW";

    internal static string MonthTitle(int year, int month) =>
        new DateTime(year, month, 1).ToString("Y", Culture());

    internal static string Time(DateTime at) => at.ToString("t", Culture());

    internal static string Date(DateTime at) => at.ToString("d", Culture());

    /// <summary>The full weekday name in the interface language, e.g. 星期三 or Wednesday.</summary>
    internal static string DayName(DateTime at) =>
        Culture().DateTimeFormat.GetDayName(at.DayOfWeek);
}
