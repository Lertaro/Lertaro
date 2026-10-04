using System.Globalization;
using LunarDay = Lunar.Lunar;
using HolidayRow = Lunar.Holiday;
using HolidayUtil = Lunar.Util.HolidayUtil;
using SolarDay = Lunar.Solar;

namespace Lertaro.Plugins.Calendar.Data;

internal enum NoteKind
{
    None,
    SolarTerm,
    LunarFestival
}

internal enum DutyKind
{
    None,
    Off,
    Work
}

/// <param name="LunarText">The lunar day under the number: 八月 for a month's first day, else 十九.</param>
/// <param name="NoteText">A 节气 or 农历节日 name that replaces <paramref name="LunarText"/>, else empty.</param>
/// <param name="DutyName">The statutory holiday this day belongs to, e.g. 国庆节, empty when there is none.</param>
/// <param name="IsToday">Set by whoever renders a month, not derived inside the data layer from the
/// clock, so the layer stays a function of the arguments handed to it and remains testable.</param>
internal readonly record struct DayInfo(
    DateTime Date,
    string LunarText,
    string NoteText,
    NoteKind Note,
    DutyKind Duty,
    string DutyName,
    bool InMonth,
    bool IsWeekend,
    bool IsToday)
{
    public bool HasNote => Note != NoteKind.None;
    public bool HasDuty => Duty != DutyKind.None;
    public string SubLabel => HasNote ? NoteText : LunarText;
}

/// <summary>
/// The traditional almanac reading of one day, for the detail column. Every value here is Chinese by
/// nature: it is the library's own datum (a 干支 pillar, a 彭祖百忌 line, a list of 宜), not UI text, so
/// it is deliberately not run through the translation files. Only the labels beside it are, and only the
/// data goes through <see cref="ChineseScript"/> when the interface language is a Traditional one.
/// </summary>
internal readonly record struct AlmanacInfo(
    DateTime Date,
    string LunarDate,
    string YearPillar,
    string MonthPillar,
    string DayPillar,
    string FiveElements,
    string ChongSha,
    string PengZuGan,
    string PengZuZhi,
    string Luck,
    string JoyGodDirection,
    string FortuneGodDirection,
    string WealthGodDirection,
    string Yi,
    string Ji,
    string LuckySpirits,
    string EvilSpirits);

/// <summary>
/// The plugin's whole view of the Chinese calendar, and the only file in the assembly allowed to touch
/// the lunar-csharp types. Everything above it consumes <see cref="DayInfo"/> strings, so the library's
/// namespace collision (namespace <c>Lunar</c> containing a class <c>Lunar</c>) stays contained here.
/// </summary>
internal static class ChineseCalendar
{
    internal const string FirstDayMonday = "Monday";
    internal const string FirstDaySunday = "Sunday";
    internal const string FirstDayCulture = "CultureDefault";

    internal const string LunarChinese = "Chinese";
    internal const string LunarNumeric = "Numeric";
    internal const string LunarHidden = "Hidden";

    /// <summary>A Windows-11 style month always fills six rows, so a month is a fixed 7 x 6 grid.</summary>
    internal const int CellsPerMonth = 42;

    private static readonly Dictionary<int, Dictionary<string, HolidayRow>> _yearDuties = new();
    private static readonly object _dutyLock = new();

    internal static DayOfWeek ResolveFirstDayOfWeek(string? configured, DayOfWeek cultureDefault) =>
        configured switch
        {
            FirstDayMonday => DayOfWeek.Monday,
            FirstDaySunday => DayOfWeek.Sunday,
            _ => cultureDefault
        };

    /// <summary>The seven days of a week, beginning wherever the user said the week begins.</summary>
    internal static IReadOnlyList<DayOfWeek> WeekdayOrder(DayOfWeek first)
    {
        var days = new DayOfWeek[7];
        for (var i = 0; i < 7; i++)
            days[i] = (DayOfWeek)(((int)first + i) % 7);
        return days;
    }

    /// <summary>
    /// The six rows of a month, starting on <paramref name="first"/>. Days outside the month come back
    /// flagged rather than blank, because the grid shows them dimmed and they still carry their lunar text.
    /// <paramref name="today"/> is passed in rather than read from the clock so the whole grid stays a
    /// deterministic function of its arguments.
    /// </summary>
    internal static IReadOnlyList<DayInfo> MonthCells(
        int year, int month, DayOfWeek first, DateTime today, string lunarDisplay, bool showDuties)
    {
        var firstOfMonth = new DateTime(year, month, 1);
        var lead = ((int)firstOfMonth.DayOfWeek - (int)first + 7) % 7;
        var start = firstOfMonth.AddDays(-lead);

        var cells = new List<DayInfo>(CellsPerMonth);
        for (var i = 0; i < CellsPerMonth; i++)
        {
            var day = start.AddDays(i);
            cells.Add(Describe(day, lunarDisplay, showDuties) with
            {
                InMonth = day.Month == month,
                IsToday = day.Date == today.Date
            });
        }
        return cells;
    }

    internal static DayInfo Describe(DateTime date, string lunarDisplay, bool showDuties)
    {
        // Solar.Lunar is a computed property that builds a fresh Lunar on every access, so it is read
        // once per cell rather than reached through for each field.
        var lunar = SolarDay.FromDate(date).Lunar;

        var note = string.Empty;
        var kind = NoteKind.None;
        var jieQi = lunar.CurrentJieQi;
        if (jieQi != null)
        {
            note = jieQi.Name;
            kind = NoteKind.SolarTerm;
        }
        else if (lunar.Festivals.Count > 0)
        {
            note = lunar.Festivals[0];
            kind = NoteKind.LunarFestival;
        }

        var duty = DutyKind.None;
        var dutyName = string.Empty;
        if (showDuties && DutyOf(date) is { } row)
        {
            duty = row.Work ? DutyKind.Work : DutyKind.Off;
            dutyName = row.Name;
        }

        return new DayInfo(
            date,
            LunarText(lunar, lunarDisplay),
            note,
            kind,
            duty,
            dutyName,
            InMonth: true,
            IsWeekend: date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
            IsToday: false);
    }

    /// <summary>
    /// The almanac reading of a day, assembled the way the reference calendar presents it: 干支 pillars
    /// with the year's zodiac, the day's 纳音五行, 冲 and 煞, the two 彭祖百忌 lines, the 值神 with its
    /// 黄道/黑道 and 吉/凶, the three auspicious directions, and the 宜/忌 and 吉神/凶神 lists.
    /// </summary>
    internal static AlmanacInfo Almanac(DateTime date)
    {
        var lunar = SolarDay.FromDate(date).Lunar;

        return new AlmanacInfo(
            date,
            $"{lunar.MonthInChinese}月{lunar.DayInChinese}",
            $"{lunar.YearInGanZhi}{lunar.YearShengXiao}年",
            $"{lunar.MonthInGanZhi}月",
            $"{lunar.DayInGanZhi}日",
            lunar.DayNaYin,
            $"冲{lunar.DayChongDesc} 煞{lunar.DaySha}",
            lunar.PengZuGan,
            lunar.PengZuZhi,
            $"{lunar.DayTianShen} {lunar.DayTianShenType} {lunar.DayTianShenLuck}",
            lunar.DayPositionXiDesc,
            lunar.DayPositionFuDesc,
            lunar.DayPositionCaiDesc,
            string.Join(" ", lunar.DayYi),
            string.Join(" ", lunar.DayJi),
            string.Join(" ", lunar.DayJiShen),
            string.Join(" ", lunar.DayXiongSha));
    }

    /// <summary>
    /// Whether the library carries any statutory-holiday rows for a year at all. It does not before the
    /// State Council's annual notice exists (usually November or December of the year before), so a month
    /// in such a year has to be drawn without 休/班 badges rather than as if every day were a workday.
    /// </summary>
    internal static bool YearHasDutyData(int year) => DutyMap(year).Count > 0;

    /// <summary>
    /// The day's lunar month on its own, e.g. 八月 -- what a caller that composes its own line out of the
    /// month and a day-or-note needs, since <see cref="DayInfo.LunarText"/> is only the month on a month's
    /// first day and only the day on every other one.
    /// </summary>
    internal static string LunarMonthText(DateTime date) => SolarDay.FromDate(date).Lunar.MonthInChinese + "月";

    /// <summary>
    /// ponytail: Numeric mode renders a leap month's M the same as its non-leap twin, because the only
    /// marker the library offers is the Chinese 闰 prefix and this mode exists for readers who would not
    /// know what that means. Leap months land on one month in roughly twenty-five. Upgrade path: carry a
    /// bool out of Lunar.Month's sign and let the caller decorate it.
    /// </summary>
    private static string LunarText(LunarDay lunar, string display) => display switch
    {
        LunarHidden => string.Empty,
        LunarNumeric => string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(lunar.Month)}/{lunar.Day}"),
        // A lunar month's first day is conventionally labelled with the month, the way the reference
        // calendar shows 八月 rather than 初一. MonthInChinese already carries 闰 for a leap month.
        _ => lunar.Day == 1 ? lunar.MonthInChinese + "月" : lunar.DayInChinese
    };

    private static HolidayRow? DutyOf(DateTime date) =>
        DutyMap(date.Year).TryGetValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var row) ? row : null;

    private static Dictionary<string, HolidayRow> DutyMap(int year)
    {
        lock (_dutyLock)
        {
            if (_yearDuties.TryGetValue(year, out var cached)) return cached;

            // Keyed by the library's own yyyy-MM-dd Day string, so the lookup above needs no conversion
            // and a year's whole table costs one scan of the library's data instead of one per cell.
            var map = new Dictionary<string, HolidayRow>(StringComparer.Ordinal);
            foreach (var row in HolidayUtil.GetHolidays(year))
                map[row.Day] = row;

            _yearDuties[year] = map;
            return map;
        }
    }
}
