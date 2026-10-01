using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;
using Lertaro.Plugins.Calendar.View;

namespace Lertaro.Plugins.Calendar.Reminders;

/// <summary>
/// The one place a reminder turns into something the user can see.
/// </summary>
/// <remarks>
/// Deliberately a single static entry point. If the host's balloon ever stops being the right vehicle, the
/// replacement is a plugin-owned window and this file is the only thing that changes.
/// </remarks>
internal static class ReminderNotifier
{
    internal static void Show(CalendarReminder reminder, bool late)
    {
        var title = late
            ? TranslationService.Format("Calendar_NotificationLate", CalendarText.Time(reminder.At))
            : TranslationService.Get("Calendar_NotificationTitle");

        PluginNotificationService.Show(title, reminder.Text, () => CalendarView.ShowOrActivate(reminder.At));
    }
}
