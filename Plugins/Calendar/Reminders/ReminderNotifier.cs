using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;
using Lertaro.Plugins.Calendar.View;

namespace Lertaro.Plugins.Calendar.Reminders;

/// <summary>
/// The one place a reminder turns into something the user can see.
/// </summary>
/// <remarks>
/// Deliberately a single static entry point, so the wording, the urgency and the time on screen are decided
/// once for every reminder instead of at each call site. How the notification is actually drawn, and for how
/// long at most, is the host's: this only says what to show.
/// </remarks>
internal static class ReminderNotifier
{
    internal static void Show(CalendarReminder reminder, bool late)
    {
        var title = late
            ? TranslationService.Format("Calendar_NotificationLate", CalendarText.Time(reminder.At))
            : TranslationService.Get("Calendar_NotificationTitle");

        // A reminder that arrives after its own moment is the one case worth warning colour for: the time it
        // asked to be told about has already passed.
        PluginNotificationService.Show(new NotificationRequest
        {
            Title = title,
            Message = reminder.Text,
            Level = late ? NotificationLevel.Warn : NotificationLevel.Info,
            DurationSeconds = CalendarPlugin.ReminderDurationSeconds(),
            OnClick = () => CalendarView.ShowOrActivate(reminder.At)
        });
    }
}
