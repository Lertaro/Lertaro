using Lertaro.Plugins.Calendar.Data;

namespace Lertaro.Plugins.Calendar.Tests.Data;

[TestClass]
public sealed class ChineseCalendarTests
{
    private static readonly DateTime FixedToday = new(2026, 9, 29);

    [TestMethod]
    [DataRow(2026, 9, 29, "十九")]
    [DataRow(2026, 9, 25, "十五")]
    [DataRow(2026, 9, 7, "廿六")]
    [DataRow(2026, 9, 13, "初三")]
    [DataRow(2026, 10, 1, "廿一")]
    public void Describe_MatchesTheReferenceLunarDays(int year, int month, int day, string expected)
    {
        var info = ChineseCalendar.Describe(new DateTime(year, month, day), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(expected, info.LunarText);
    }

    [TestMethod]
    [DataRow(2026, 9, 11, "八月")]
    [DataRow(2026, 2, 17, "正月")]
    public void Describe_FirstDayOfALunarMonthIsLabelledWithTheMonth(int year, int month, int day, string expected)
    {
        // The reference calendar shows 八月 under 11 September, not 初一; a month's first day is
        // conventionally named for the month.
        var info = ChineseCalendar.Describe(new DateTime(year, month, day), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(expected, info.LunarText);
    }

    [TestMethod]
    [DataRow(2026, 9, 7, "白露")]
    [DataRow(2026, 9, 23, "秋分")]
    public void Describe_SolarTermReplacesTheLunarDay(int year, int month, int day, string expected)
    {
        var info = ChineseCalendar.Describe(new DateTime(year, month, day), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(NoteKind.SolarTerm, info.Note);
        Assert.AreEqual(expected, info.NoteText);
        Assert.AreEqual(expected, info.SubLabel);
    }

    [TestMethod]
    public void Describe_MidAutumnShowsTheFestivalNotTheLunarDay()
    {
        var info = ChineseCalendar.Describe(new DateTime(2026, 9, 25), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(NoteKind.LunarFestival, info.Note);
        Assert.AreEqual("中秋节", info.SubLabel);
    }

    [TestMethod]
    public void Describe_NationalDayKeepsTheLunarDayAndCarriesTheOffBadge()
    {
        // A solar holiday is not a lunar label: the reference calendar keeps 廿一 under 1 October and
        // says nothing about 国庆节, so the holiday has to reach the user through the badge instead.
        var info = ChineseCalendar.Describe(new DateTime(2026, 10, 1), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(NoteKind.None, info.Note);
        Assert.AreEqual("廿一", info.SubLabel);
        Assert.AreEqual(DutyKind.Off, info.Duty);
        Assert.AreEqual("国庆节", info.DutyName);
    }

    [TestMethod]
    [DataRow(2026, 9, 20, "Work")]
    [DataRow(2026, 9, 25, "Off")]
    [DataRow(2026, 9, 26, "Off")]
    [DataRow(2026, 10, 10, "Work")]
    [DataRow(2026, 9, 29, "None")]
    public void Describe_ReportsStatutoryDutyIncludingMakeUpDays(int year, int month, int day, string expected)
    {
        var info = ChineseCalendar.Describe(new DateTime(year, month, day), ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(expected, info.Duty.ToString());
    }

    [TestMethod]
    public void Describe_WithDutiesOffReportsNoBadge()
    {
        var info = ChineseCalendar.Describe(new DateTime(2026, 10, 1), ChineseCalendar.LunarChinese, false);

        Assert.AreEqual(DutyKind.None, info.Duty);
        Assert.IsFalse(info.HasDuty);
    }

    [TestMethod]
    public void Describe_NumericAndHiddenLunarModes()
    {
        Assert.AreEqual("8/19", ChineseCalendar.Describe(FixedToday, ChineseCalendar.LunarNumeric, true).LunarText);
        Assert.AreEqual(string.Empty, ChineseCalendar.Describe(FixedToday, ChineseCalendar.LunarHidden, true).LunarText);
        Assert.AreEqual("十九", ChineseCalendar.Describe(FixedToday, ChineseCalendar.LunarChinese, true).LunarText);
    }

    [TestMethod]
    public void ResolveFirstDayOfWeek_ConfiguredValueBeatsTheCulture()
    {
        Assert.AreEqual(DayOfWeek.Monday, ChineseCalendar.ResolveFirstDayOfWeek("Monday", DayOfWeek.Sunday));
        Assert.AreEqual(DayOfWeek.Sunday, ChineseCalendar.ResolveFirstDayOfWeek("Sunday", DayOfWeek.Monday));
        Assert.AreEqual(DayOfWeek.Tuesday, ChineseCalendar.ResolveFirstDayOfWeek("CultureDefault", DayOfWeek.Tuesday));
        Assert.AreEqual(DayOfWeek.Monday, ChineseCalendar.ResolveFirstDayOfWeek(null, DayOfWeek.Monday));
        Assert.AreEqual(DayOfWeek.Monday, ChineseCalendar.ResolveFirstDayOfWeek("not a choice", DayOfWeek.Monday));
    }

    [TestMethod]
    public void WeekdayOrder_StartsWhereTheWeekStarts()
    {
        CollectionAssert.AreEqual(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday },
            ChineseCalendar.WeekdayOrder(DayOfWeek.Monday).ToList());
        CollectionAssert.AreEqual(
            new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday },
            ChineseCalendar.WeekdayOrder(DayOfWeek.Sunday).ToList());
    }

    [TestMethod]
    [DataRow(2026)]
    [DataRow(2027)]
    public void MonthCells_AlwaysFillSixRows(int year)
    {
        for (var month = 1; month <= 12; month++)
        {
            var cells = ChineseCalendar.MonthCells(year, month, DayOfWeek.Monday, FixedToday, ChineseCalendar.LunarChinese, true);
            Assert.AreEqual(ChineseCalendar.CellsPerMonth, cells.Count, $"{year}-{month:D2}");
        }
    }

    [TestMethod]
    [DataRow(DayOfWeek.Monday)]
    [DataRow(DayOfWeek.Sunday)]
    public void MonthCells_StartsOnTheConfiguredDay(DayOfWeek first)
    {
        for (var month = 1; month <= 12; month++)
        {
            var cells = ChineseCalendar.MonthCells(2026, month, first, FixedToday, ChineseCalendar.LunarChinese, true);
            Assert.AreEqual(first, cells[0].Date.DayOfWeek, $"month {month}");
        }
    }

    [TestMethod]
    public void MonthCells_EveryDayOfMonthAppearsExactlyOnce()
    {
        for (var month = 1; month <= 12; month++)
        {
            var cells = ChineseCalendar.MonthCells(2026, month, DayOfWeek.Monday, FixedToday, ChineseCalendar.LunarChinese, true)
                .Where(c => c.InMonth)
                .Select(c => c.Date.Day)
                .ToList();

            var daysInMonth = DateTime.DaysInMonth(2026, month);
            Assert.AreEqual(daysInMonth, cells.Count, $"month {month} in-month cell count");
            CollectionAssert.AreEqual(Enumerable.Range(1, daysInMonth).ToList(), cells, $"month {month} days");
        }
    }

    [TestMethod]
    public void MonthCells_LeapsIntoFebruaryOnALeapYear()
    {
        var cells = ChineseCalendar.MonthCells(2028, 2, DayOfWeek.Monday, FixedToday, ChineseCalendar.LunarChinese, true);

        Assert.AreEqual(42, cells.Count);
        Assert.AreEqual(29, cells.Count(c => c.InMonth));
    }

    [TestMethod]
    public void MonthCells_FlagsTodayAndWeekends()
    {
        var cells = ChineseCalendar.MonthCells(2026, 9, DayOfWeek.Monday, FixedToday, ChineseCalendar.LunarChinese, true);

        var today = cells.Single(c => c.IsToday);
        Assert.AreEqual(new DateTime(2026, 9, 29), today.Date);
        // Six rows starting on Monday each hold exactly one Sunday, and the row that begins on
        // 31 August is one of them, so the grid shows six rather than the month's own four.
        Assert.AreEqual(6, cells.Count(c => c.Date.DayOfWeek == DayOfWeek.Sunday));
        Assert.AreEqual(4, cells.Count(c => c.Date.DayOfWeek == DayOfWeek.Sunday && c.InMonth));
        Assert.IsTrue(cells.Single(c => c.Date.Day == 27).IsWeekend);
        Assert.IsFalse(cells.Single(c => c.Date.Day == 29).IsWeekend);
    }

    [TestMethod]
    public void YearHasDutyData_KnowsThatANotYetAnnouncedYearIsEmpty()
    {
        // The State Council notice for 2027 does not exist yet, so the grid has to be able to say so
        // rather than render every day as an ordinary workday.
        Assert.IsTrue(ChineseCalendar.YearHasDutyData(2026));
        Assert.IsFalse(ChineseCalendar.YearHasDutyData(2027));
    }
}
