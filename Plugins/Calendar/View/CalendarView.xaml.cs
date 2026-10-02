using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;
using Lertaro.Plugins.Calendar.Data;
using Lertaro.Plugins.Calendar.Reminders;

namespace Lertaro.Plugins.Calendar.View;

internal sealed record DayCell(int Day, string SubLabel, bool ShowAccent, string DutyBadgeText,
    bool HasDuty, bool InMonth, bool IsToday, bool IsSelected, bool HasReminder, DateTime Date);

internal sealed record ReminderItemView(string Id, string TimeText, string Text, string DeleteLabel);

/// <summary>
/// The month grid and the reminder list for the day selected in it.
/// </summary>
/// <remarks>
/// The whole grid is rebuilt from scratch on every change instead of being kept in sync through change
/// notification. It is 42 fixed cells plus a handful of reminders, so a rebuild is cheaper to read than an
/// incremental update and there is no stale-state class of bug left to find.
/// </remarks>
public partial class CalendarView : UserControl
{
    private static PluginWindow? _window;
    private static CalendarView? _instance;

    private int _year;
    private int _month;
    private DateTime _selected = DateTime.Today;

    /// <summary>The sentence currently written inside the reminder box, in the language it was written in. Held
    /// so a language switch can replace its own copy and so the Add path can tell that text from what the user
    /// typed. See <see cref="ApplyLabels"/>.</summary>
    private string _advice = string.Empty;

    /// <summary>
    /// Read per rebuild rather than cached at construction, so moving the setting takes effect on the open
    /// window instead of only on the next one.
    /// </summary>
    private DayOfWeek FirstDayOfWeek => ChineseCalendar.ResolveFirstDayOfWeek(
        CalendarPlugin.FirstDayOfWeekSetting(), CalendarText.Culture().DateTimeFormat.FirstDayOfWeek);

    public CalendarView()
    {
        InitializeComponent();

        ApplyLabels();

        // Pre-fill the moment the window was opened, so the common case is "pick a day, type what, Add".
        // Written in the same fixed HH:mm shape TryParseTime reads back, not the culture's own time
        // pattern, which can be single-digit or carry an AM/PM the parser would reject.
        TimeInput.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

        Unloaded += (_, _) =>
        {
            TranslationService.CultureChanged -= CultureChanged;
            PluginSettingsService.SettingChanged -= SettingChanged;
        };
        TranslationService.CultureChanged += CultureChanged;
        PluginSettingsService.SettingChanged += SettingChanged;

        // The Add button says whether the row is usable rather than accepting the click and doing
        // nothing, so a mistyped time never looks like a reminder that was silently dropped.
        TimeInput.TextChanged += (_, _) => AddReminderButton.IsEnabled = CanAdd();
        ReminderText.TextChanged += (_, _) => AddReminderButton.IsEnabled = CanAdd();
        // Clicking or tabbing into a box that still holds the advice has to let the first keystroke replace it,
        // or the reminder is spelled in front of the sentence and reads "买牛奶不推荐超过5条". Once there is real
        // text in the box the caret stays exactly where the pointer put it.
        ReminderText.GotKeyboardFocus += (_, _) =>
        {
            if (string.Equals(ReminderText.Text, _advice, StringComparison.Ordinal)) ReminderText.SelectAll();
        };

        AddReminderButton.IsEnabled = CanAdd();
        UpdatePinButton();
        FocusDate(DateTime.Today);
    }

    /// <summary>
    /// Every string the view owns. Shared by construction and by a language switch so a label added to one
    /// cannot be quietly left out of the other.
    /// </summary>
    private void ApplyLabels()
    {
        PrevMonthButton.ToolTip = TranslationService.Get("Calendar_PrevMonth");
        NextMonthButton.ToolTip = TranslationService.Get("Calendar_NextMonth");
        TodayButton.Content = TranslationService.Get("Calendar_Today");
        AddReminderButton.Content = TranslationService.Get("Calendar_Add");
        TimePromptText.Text = TranslationService.Get("Calendar_TimeHint");
        NoRemindersText.Text = TranslationService.Get("Calendar_NoReminders");
        ReminderText.ToolTip = TranslationService.Get("Calendar_TextHint");
        TimeInput.ToolTip = TranslationService.Get("Calendar_TimeHint");

        var advice = TranslationService.Get("Calendar_TextAdvice");
        // The advice about how many reminders are sensible goes inside the box rather than beside it, because
        // that is where the eye already is. It is offered once, on the window's construction -- the box is empty
        // then -- and again on a language switch, where the only text allowed to be overwritten is the copy we
        // put there ourselves. Once the user has typed over it, or added a reminder and emptied the box, it is
        // never restored from here: the row is theirs after that.
        if (string.IsNullOrWhiteSpace(ReminderText.Text) || ReminderText.Text == _advice)
            ReminderText.Text = advice;
        _advice = advice;
    }

    private void SettingChanged(string pluginId, string _)
    {
        if (!string.Equals(pluginId, CalendarPlugin.PluginId, StringComparison.OrdinalIgnoreCase)) return;
        if (Dispatcher.CheckAccess()) Rebuild();
        else Dispatcher.BeginInvoke(Rebuild);
    }

    private void CultureChanged(string _)
    {
        if (Dispatcher.CheckAccess()) Reload();
        else Dispatcher.BeginInvoke(Reload);
    }

    private void Reload()
    {
        ApplyLabels();
        _window?.Title = TranslationService.Get("Calendar_WindowTitle");
        UpdatePinButton();
        Rebuild();
    }

    /// <summary>Opens the calendar on <paramref name="focusDay"/>, reusing the one window.</summary>
    internal static void ShowOrActivate(DateTime focusDay)
    {
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => ShowOrActivate(focusDay));
            return;
        }

        if (_window == null || _instance == null)
        {
            var view = new CalendarView();
            var window = new PluginWindow(
                TranslationService.Get("Calendar_WindowTitle"), 860, 700, PluginWindowMode.Window);
            // The calendar has no confirm/cancel actions, so the footer strip is 52 rows of nothing.
            window.ShowFooter = false;
            // A ContentControl aligns its content to the top by default, which leaves the view at its
            // desired height and starves the reminder row that is meant to absorb the window's slack.
            window.ContentHostControl.VerticalContentAlignment = VerticalAlignment.Stretch;
            // The same trap horizontally: left-aligned content is arranged at its desired width, so the
            // star column never receives the window's slack and everything to its right is cut short
            // instead of spreading into it.
            window.ContentHostControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            window.ContentHostControl.Content = view;
            window.Closed += (_, _) =>
            {
                _window = null;
                _instance = null;
            };
            _window = window;
            _instance = view;
            window.Show();
        }

        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _instance.FocusDate(focusDay);
        _window.Activate();
    }

    internal void FocusDate(DateTime date)
    {
        _year = date.Year;
        _month = date.Month;
        _selected = date.Date;
        Rebuild();
    }

    private void Rebuild()
    {
        // Resolved once per rebuild so the header row and the grid can never disagree about where the
        // week starts, even if the setting changes while this is running.
        var first = FirstDayOfWeek;
        BuildWeekdayHeader(first);

        MonthTitleText.Text = CalendarText.MonthTitle(_year, _month);

        // Outside the Chinese interface languages the whole lunar layer is withdrawn. 节气/节日 names and the
        // 休/班 badges are almanac data with no translation behind them, so blanking only the lunar text would
        // leave them on screen looking like an unfinished locale.
        var chinese = CalendarText.ShowsChineseCalendar;
        var display = chinese ? CalendarPlugin.LunarDisplaySetting() : ChineseCalendar.LunarHidden;
        var showDuties = chinese && CalendarPlugin.ShowStatutoryHolidays();
        var cells = ChineseCalendar.MonthCells(_year, _month, first, DateTime.Today, display, showDuties);
        var yearHasDuties = !showDuties || ChineseCalendar.YearHasDutyData(_year);

        // One snapshot feeds both the reminder list and the set that tints the cells, so the two can never
        // disagree about what is scheduled.
        var scheduled = CalendarPlugin.Store?.Snapshot() ?? Array.Empty<CalendarReminder>();
        var busy = scheduled.Select(r => r.At.Date).ToHashSet();

        DayGrid.ItemsSource = cells
            .Select(ChineseScript.ForRegion)
            .Select(c => new DayCell(
                c.Date.Day,
                chinese ? c.SubLabel : string.Empty,
                chinese && c.HasNote,
                c.HasDuty ? DutyBadge(c.Duty) : string.Empty,
                c.HasDuty,
                c.InMonth,
                c.IsToday,
                c.Date.Date == _selected,
                busy.Contains(c.Date.Date),
                c.Date.Date))
            .ToList();

        SelectedDateText.Text = CalendarText.Date(_selected);
        FootnoteText.Text = TranslationService.Get("Calendar_NoHolidayData");
        FootnoteText.Visibility = yearHasDuties ? Visibility.Collapsed : Visibility.Visible;

        // The delete affordance is an icon with no text beside it, so it needs a named label of its own.
        var deleteLabel = TranslationService.Get("Calendar_Delete");
        AlmanacColumn.Visibility = chinese ? Visibility.Visible : Visibility.Collapsed;
        if (chinese) AlmanacColumn.Apply(ChineseScript.ForRegion(ChineseCalendar.Almanac(_selected)));

        var forDay = scheduled.Where(r => r.At.Date == _selected).OrderBy(r => r.At).ToList();
        ReminderList.ItemsSource = forDay
            .Select(r => new ReminderItemView(r.Id, CalendarText.Time(r.At), r.Text, deleteLabel))
            .ToList();
        NoRemindersText.Visibility = forDay.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string DutyBadge(DutyKind duty) =>
        TranslationService.Get(duty == DutyKind.Work ? "Calendar_BadgeWork" : "Calendar_BadgeOff");

    private void BuildWeekdayHeader(DayOfWeek first)
    {
        WeekdayHeader.Children.Clear();
        var format = CalendarText.Culture().DateTimeFormat;
        foreach (var day in ChineseCalendar.WeekdayOrder(first))
        {
            var label = new TextBlock
            {
                Text = format.GetAbbreviatedDayName(day),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            WeekdayHeader.Children.Add(label);
        }
    }

    private void PrevMonth_Click(object sender, RoutedEventArgs e) => MoveMonth(-1);
    private void NextMonth_Click(object sender, RoutedEventArgs e) => MoveMonth(1);
    private void Today_Click(object sender, RoutedEventArgs e) => FocusDate(DateTime.Today);

    /// <summary>
    /// Keeps the calendar above other windows. The host's own window frame has no pin affordance and is
    /// shared by every plugin, so the toggle lives here rather than in <c>PluginWindow</c>.
    /// </summary>
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window) return;
        window.Topmost = !window.Topmost;
        UpdatePinButton();
    }

    private void UpdatePinButton()
    {
        var pinned = Window.GetWindow(this)?.Topmost == true;
        PinButton.ToolTip = TranslationService.Get(pinned ? "Calendar_Unpin" : "Calendar_Pin");
        AutomationProperties.SetName(PinButton, PinButton.ToolTip as string ?? string.Empty);
        // The shared button style has no checked state, so the accent colour is what carries the toggle.
        PinButton.SetResourceReference(
            ForegroundProperty, pinned ? "AccentBlue" : "TextSecondary");
    }

    private void MoveMonth(int delta)
    {
        var target = new DateTime(_year, _month, 1).AddMonths(delta);
        _year = target.Year;
        _month = target.Month;
        Rebuild();
    }

    private void DayCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DayCell cell })
        {
            _selected = cell.Date;
            Rebuild();
        }
    }

    private bool TryParseTime(out DateTime time) =>
        DateTime.TryParseExact(TimeInput.Text?.Trim(), "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);

    /// <summary>Whether the box holds something to add. The advice the window opens with is text in the box like
    /// any other, so it has to be ruled out here and at the add itself: without that, pressing Enter without
    /// typing files a reminder whose text is the advice.</summary>
    private bool IsUsableText => !string.IsNullOrWhiteSpace(ReminderText.Text)
        && !string.Equals(ReminderText.Text, _advice, StringComparison.Ordinal);

    private bool CanAdd() => TryParseTime(out _) && IsUsableText;

    private void AddReminder_Click(object sender, RoutedEventArgs e) => AddReminder();

    private void ReminderText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddReminder();
    }

    private void AddReminder()
    {
        if (!TryParseTime(out var time) || !IsUsableText) return;
        if (CalendarPlugin.Store == null) return;

        var at = _selected.AddHours(time.Hour).AddMinutes(time.Minute);
        CalendarPlugin.Store.Add(at, ReminderText.Text);
        ReminderText.Text = string.Empty;
        Rebuild();

        // Off the UI thread: the file write and the catch-up pass are the same ones a tick performs, and
        // a reminder set for a moment already past should go out now rather than on the next poll.
        CalendarPlugin.Engine?.ReconcileSoon();
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id }) return;
        CalendarPlugin.Store?.Remove(id);
        Rebuild();
    }
}
