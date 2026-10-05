using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar;

/// <summary>
/// Describes a day in Chinese-calendar terms for the host's own search-box clock line.
/// </summary>
/// <remarks>
/// The host shows a date and time in the quick window's empty placeholder slot; this is what puts 农历, the
/// 节气 and the festivals beside it. Answered from the same <see cref="ChineseCalendar"/> the month grid and
/// the trigger-word row are built from, so the three can never disagree about what day it is -- which is the
/// whole reason the host asks a plugin instead of carrying a lunar table of its own.
/// </remarks>
public sealed class CalendarTextProvider : ICalendarTextProvider
{
    public string Name => TranslationService.Get("Calendar_PluginName");
    public string Description => TranslationService.Get("Calendar_TextProviderDesc");

    /// <summary>
    /// 农历 month and day, with a 节气 or festival taking the day's place when one falls on that date --
    /// 八月廿四 normally, 八月十五中秋 on Mid-Autumn, 八月白露 when the term lands there. The month grid's own
    /// "Lunar text" setting is deliberately not consulted: that one is about the second line under each date,
    /// while this is a one-line answer for the host's clock, and it has its own switch beside it.
    /// </summary>
    public string GetCalendarText(DateTime date)
    {
        // Two independent gates, and both belong to this plugin rather than to the host: the calendar layer is
        // a Chinese one, and the clock line can be switched off in this plugin's own settings. Neither is
        // expressible host-side -- a host flag would still be asking a plugin the user had disabled, which is
        // exactly what the "don't apply a disabled plugin's data" rule is meant to prevent.
        if (!CalendarText.ShowsChineseCalendar || !CalendarPlugin.ShowLunarInClock())
            return string.Empty;

        var info = ChineseCalendar.Describe(date, ChineseCalendar.LunarChinese, showDuties: false);
        // A term or festival replaces the day outright, reading 八月白露 / 八月中秋节. Otherwise the day goes
        // after the month -- except on a lunar month's first day, where the calendar already names the month
        // rather than the day and 八月初一 is what a 农历 line writes there.
        if (!info.HasNote && info.LunarText.EndsWith("月", StringComparison.Ordinal))
            return ChineseScript.ForRegion(info.LunarText + "初一");

        var day = info.HasNote ? info.NoteText : info.LunarText;
        return ChineseScript.ForRegion(ChineseCalendar.LunarMonthText(date) + day);
    }
}
