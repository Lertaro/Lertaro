using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.Calendar.Data;
using Lertaro.Plugins.Calendar.View;

namespace Lertaro.Plugins.Calendar;

/// <summary>
/// The trigger-word row that opens the calendar, and the only way into it from the search box.
/// </summary>
public sealed class CalendarInstantProvider : IInstantResultProvider
{
    private const string CalendarIcon =
        "M19 3h-1V1h-2v2H8V1H6v2H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11zM7 10h5v5H7z";

    public string Name => TranslationService.Get("Calendar_ProviderName");
    public string Description => TranslationService.Get("Calendar_ProviderDesc");

    public IReadOnlyList<string> QueryTriggerKeywords => [CalendarPlugin.TriggerKeyword()];

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var word = CalendarPlugin.TriggerKeyword();
        if (word.Length == 0)
            yield break;

        // TryMatch, not TryMatchInvoked: the bare word is the whole feature, so "cal" alone has to be
        // enough to bring the row up.
        if (!TriggerWord.TryMatch(query, word, out _))
            yield break;

        var today = ChineseScript.ForRegion(ChineseCalendar.Describe(
            DateTime.Today, CalendarPlugin.LunarDisplaySetting(), CalendarPlugin.ShowStatutoryHolidays()));

        yield return new InstantResultItem
        {
            Title = TranslationService.Get("Calendar_OpenRow"),
            Description = Describe(today),
            IconData = CalendarIcon,
            IconColor = "AccentBlue",
            ActionType = "None",
            // True so the launcher hides itself and hands foreground to the calendar rather than
            // restoring whatever was behind it, which would immediately deactivate the new window.
            OnExecuteFunc = () =>
            {
                CalendarView.ShowOrActivate(DateTime.Today);
                return true;
            }
        };
    }

    private static string Describe(DayInfo today)
    {
        var text = CalendarText.Date(today.Date);
        if (today.SubLabel.Length > 0)
            text += " " + today.SubLabel;
        if (today.HasDuty)
            text += " " + TranslationService.Format(
                today.Duty == DutyKind.Work ? "Calendar_DutyWork" : "Calendar_DutyOff", today.DutyName);
        return text;
    }
}
