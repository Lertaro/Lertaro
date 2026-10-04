using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lertaro.Plugins.Calendar.View;

/// <summary>
/// The reminder's two time fields and every gesture they answer to: the wheel, the arrow keys, typing two
/// digits, and reading the pair back as a time of day.
/// </summary>
/// <remarks>
/// Split out purely to keep <see cref="CalendarView"/> under the repository's per-file line limit; this class
/// has no state of its own beyond the two boxes it was handed, and always operates on those.
/// </remarks>
internal sealed class ReminderTimeFieldSupport
{
    private readonly TextBox _hour;
    private readonly TextBox _minute;

    /// <summary>Runs whenever either half changes, so the view can re-decide whether Add is usable.</summary>
    private readonly Action _onTimeChanged;

    /// <summary>
    /// Set while the fields are written from code (the wheel, an arrow key), so such a write does not count as
    /// typing and bounce the caret to the other half.
    /// </summary>
    private bool _writing;

    internal ReminderTimeFieldSupport(TextBox hour, TextBox minute, Action onTimeChanged)
    {
        _hour = hour;
        _minute = minute;
        _onTimeChanged = onTimeChanged;
    }

    /// <summary>Names both halves for a screen reader; the frame around them carries no label of its own.</summary>
    internal void SetAccessibleName(string name)
    {
        AutomationProperties.SetName(_hour, name);
        AutomationProperties.SetName(_minute, name);
    }

    /// <summary>
    /// Writes both halves at once, in the fixed invariant shape <see cref="TryGetTime"/> reads back, so a
    /// culture with its own digits or its own time pattern cannot produce text this control would refuse.
    /// </summary>
    internal void Set(int hour, int minute)
    {
        _writing = true;
        try
        {
            _hour.Text = hour.ToString("00", CultureInfo.InvariantCulture);
            _minute.Text = minute.ToString("00", CultureInfo.InvariantCulture);
        }
        finally
        {
            _writing = false;
        }
    }

    /// <summary>
    /// Reads the two halves as a time of day. Deliberately not the culture's own time pattern: the fields are
    /// plain 0-23 and 0-59 numbers, and a single digit is accepted rather than rejected for its shape.
    /// </summary>
    internal bool TryGetTime(out DateTime time)
    {
        time = default;
        if (!TryRead(_hour, 23, out var hour) || !TryRead(_minute, 59, out var minute))
            return false;

        // Only the time of day matters: the view puts these on the day selected in the grid.
        time = new DateTime(1, 1, 1, hour, minute, 0);
        return true;
    }

    internal void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox field)
            Step(field, forward: e.Delta > 0);

        // Handled even when nothing changed, so the wheel never falls through to whatever scrolls behind the
        // panel while the pointer is over this control.
        e.Handled = true;
    }

    internal void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox field) return;

        switch (e.Key)
        {
            // Up and Down take the same one-notch step the wheel takes, so the two gestures agree on what
            // "one more" means.
            case Key.Up:
                Step(field, forward: true);
                e.Handled = true;
                break;
            case Key.Down:
                Step(field, forward: false);
                e.Handled = true;
                break;
            // Left and Right cross between the halves rather than walking the caret out of the control: at two
            // characters there is nowhere inside one field worth moving to.
            case Key.Left when ReferenceEquals(field, _minute):
                FocusCaretAtEnd(_hour);
                e.Handled = true;
                break;
            case Key.Right when ReferenceEquals(field, _hour):
                FocusCaretAtEnd(_minute);
                e.Handled = true;
                break;
        }
    }

    internal void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Selecting on entry makes typing replace the half instead of inserting into it, which is what lets
        // "focus the hour and type 09" work without clearing the old value first.
        if (sender is TextBox field)
            field.SelectAll();
    }

    internal void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _onTimeChanged();

        // A write from Step is not typing, and must not move the caret to the other half.
        if (_writing) return;

        // Two digits in means this half is finished, so the minute is where the next keystroke belongs. Only
        // when it was typed here: a value set from elsewhere never moves the caret.
        if (sender is TextBox { IsKeyboardFocusWithin: true, MaxLength: 2 } field
            && field.Text.Length == field.MaxLength
            && ReferenceEquals(field, _hour))
        {
            _minute.Focus();
            _minute.SelectAll();
        }
    }

    /// <summary>
    /// Moves one half by a single notch, the one step the wheel and the arrow keys share.
    /// </summary>
    /// <remarks>
    /// A half holding something unreadable restarts from the current time rather than swallowing the gesture,
    /// so either input is always a way back to a usable value. Both halves are written, because the pair is
    /// what a valid time is made of.
    /// </remarks>
    private void Step(TextBox field, bool forward)
    {
        var isHour = ReferenceEquals(field, _hour);
        var baseTime = TryGetTime(out var parsed) ? parsed : DateTime.Now;

        var (hour, minute) = ReminderTimeSpinner.Adjust(baseTime.Hour, baseTime.Minute, isHour, forward);
        Set(hour, minute);

        // Put the caret back in the half that was stepped, so holding the gesture keeps moving that one.
        field.CaretIndex = field.Text.Length;
    }

    /// <summary>
    /// One field as a number within <paramref name="max"/>. <see cref="NumberStyles.None"/> is what rules out
    /// the signs, thousands separators and stray whitespace that would make "1 2" or "-1" look acceptable.
    /// </summary>
    private static bool TryRead(TextBox field, int max, out int value) =>
        int.TryParse(field.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value <= max;

    /// <summary>Focuses a field with the caret after its text, so the next arrow key keeps moving forward.</summary>
    private static void FocusCaretAtEnd(TextBox field)
    {
        field.Focus();
        field.CaretIndex = field.Text.Length;
    }
}
