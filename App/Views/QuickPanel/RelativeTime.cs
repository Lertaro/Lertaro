using Lertaro.App.Services;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>How long ago a file was touched, in words.</summary>
/// <remarks>
/// Came over from the startup panel's Recent Files tab, which is where it was written and which no
/// longer exists; the quick panel had been borrowing it from there. Every string is a format from the
/// translations, so the unit names and their order follow the UI language rather than being assembled
/// out of English words here.
///
/// The smaller unit is dropped when it is zero, so a file touched exactly two hours ago reads as
/// "2 hours ago" rather than "2 hours 0 minutes ago".
/// </remarks>
internal static class RelativeTime
{
    public static string Describe(DateTime modified)
    {
        if (modified == DateTime.MinValue) return string.Empty;

        var totalSeconds = (long)Math.Max(0, (DateTime.Now - modified).TotalSeconds);

        if (totalSeconds < 60)
            return string.Format(TranslationManager.Instance["QuickPanel_SecondsAgo"], totalSeconds);

        var totalMinutes = totalSeconds / 60;
        if (totalMinutes < 60)
            return string.Format(TranslationManager.Instance["QuickPanel_MinutesAgo"], totalMinutes);

        if (totalMinutes < 1440)
        {
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0
                ? string.Format(TranslationManager.Instance["QuickPanel_HoursAgo"], hours)
                : string.Format(TranslationManager.Instance["QuickPanel_HoursMinutesAgo"], hours, minutes);
        }

        var days = totalMinutes / 1440;
        var remHours = (totalMinutes % 1440) / 60;
        return remHours == 0
            ? string.Format(TranslationManager.Instance["QuickPanel_DaysAgo"], days)
            : string.Format(TranslationManager.Instance["QuickPanel_DaysHoursAgo"], days, remHours);
    }
}
