using Lertaro.Plugins.Calendar.View;

namespace Lertaro.Plugins.Calendar.Tests.View;

/// <summary>
/// Covers the reminder time fields' step arithmetic: where one notch lands the hour, and where it lands the
/// minute.
/// </summary>
[TestClass]
public sealed class ReminderTimeSpinnerTests
{
    [TestMethod]
    [DataRow(9, true, 10)]
    [DataRow(0, true, 1)]
    [DataRow(22, true, 23)]
    // Wraps inside the day rather than carrying into the date: the reminder's day comes from the calendar
    // grid, so a time that rolled over midnight would set a reminder on a different day than the one shown.
    [DataRow(23, true, 0)]
    [DataRow(0, false, 23)]
    [DataRow(23, false, 22)]
    public void Adjust_HourMovesOneAtATimeAndWrapsWithinTheDay(int hour, bool forward, int expected)
    {
        var (actualHour, _) = ReminderTimeSpinner.Adjust(hour, 30, isHour: true, forward);

        Assert.AreEqual(expected, actualHour);
    }

    [TestMethod]
    public void Adjust_HourLeavesTheMinuteAlone()
    {
        var (_, minute) = ReminderTimeSpinner.Adjust(9, 37, isHour: true, forward: true);

        Assert.AreEqual(37, minute);
    }

    [TestMethod]
    [DataRow(0, true, 1)]
    [DataRow(30, true, 31)]
    [DataRow(58, true, 59)]
    [DataRow(59, true, 0)]
    [DataRow(31, false, 30)]
    [DataRow(1, false, 0)]
    [DataRow(0, false, 59)]
    public void Adjust_MinuteMovesOneAtATimeAndWrapsWithinTheHour(int minute, bool forward, int expected)
    {
        // One per notch: every value in the hour is reachable, and a notch never skips over one.
        var (_, actualMinute) = ReminderTimeSpinner.Adjust(9, minute, isHour: false, forward);

        Assert.AreEqual(expected, actualMinute);
    }

    [TestMethod]
    public void Adjust_MinuteLeavesTheHourAlone()
    {
        // :59 is the boundary the wrap happens at, so it is also where a carry into the hour would show up.
        var (hour, _) = ReminderTimeSpinner.Adjust(9, 59, isHour: false, forward: true);

        Assert.AreEqual(9, hour);
    }
}
