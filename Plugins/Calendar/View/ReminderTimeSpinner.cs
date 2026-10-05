namespace Lertaro.Plugins.Calendar.View;

/// <summary>
/// The arithmetic behind the reminder time fields: what one wheel notch, or one arrow press, does to the hour
/// or to the minute.
/// </summary>
/// <remarks>
/// Pure and separate from the view so the wrapping can be tested without a window. The view owns the pointer,
/// the focus and the text; this owns only the numbers.
/// </remarks>
internal static class ReminderTimeSpinner
{
    /// <summary>
    /// <paramref name="hour"/> and <paramref name="minute"/> moved one notch on whichever field
    /// <paramref name="isHour"/> selects. Both step by one, so a notch always means the next or the previous
    /// value.
    /// </summary>
    /// <remarks>
    /// The two fields wrap inside their own range instead of carrying into each other, so stepping the minutes
    /// up from :59 gives :00 and not the next hour. A reminder's date comes from the day selected in the grid,
    /// so silently rolling past midnight would be setting a reminder on a different day than the one on
    /// screen.
    /// </remarks>
    internal static (int Hour, int Minute) Adjust(int hour, int minute, bool isHour, bool forward) =>
        isHour
            ? (Wrap(hour + (forward ? 1 : -1), 24), minute)
            : (hour, Wrap(minute + (forward ? 1 : -1), 60));

    private static int Wrap(int value, int range) => (value % range + range) % range;
}
