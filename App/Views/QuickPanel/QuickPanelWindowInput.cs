using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lertaro.App.Views.QuickPanel;

// The decisions the panel's input handlers make, as functions of what was pressed rather than of the
// window's own state. Split out of QuickPanelWindow.xaml.cs purely to keep that file under the repo's
// per-file line limit; every one of these is static, and they are here rather than there because they
// are also the only parts of the panel's input worth testing without a mouse.
public partial class QuickPanelWindow
{
    /// <summary>
    /// Whether this summon has been asked not to dismiss itself when it loses the foreground.
    /// </summary>
    /// <remarks>
    /// On the window, not the view model, and that is the whole of the "current summon only" scoping the
    /// quick window needs an explicit reset for: this window IS the summon, so the flag dies with it and
    /// the next open starts normal without anyone having to remember to clear it.
    /// </remarks>
    public static readonly DependencyProperty IsStayOpenProperty = DependencyProperty.Register(
        nameof(IsStayOpen), typeof(bool), typeof(QuickPanelWindow), new PropertyMetadata(false));

    public bool IsStayOpen
    {
        get => (bool)GetValue(IsStayOpenProperty);
        set => SetValue(IsStayOpenProperty, value);
    }

    public void ToggleStayOpen() => IsStayOpen = !IsStayOpen;

    /// <summary>Which workspace a keystroke asks for, 1-based, or 0 for anything that is not the shortcut.</summary>
    /// <remarks>
    /// The same modifier the result lists use for "jump to result N", rather than a second setting that
    /// would only ever be set to the same thing -- one "hold this and press a number" key everywhere.
    /// </remarks>
    internal static int WorkspaceIndexFor(Key key, ModifierKeys modifiers, string? jumpModifier)
    {
        if (string.IsNullOrEmpty(jumpModifier)) return 0;

        // The numpad row counts as the same number: compared as the digit it typed rather than as the
        // key it came from.
        var digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (digit == 0) return 0;

        // "D3", not "3". A combo string is a Key.ToString() spelling -- that is what the hotkey recorder
        // writes and what TryParseHotkey reads back -- and a bare digit parses as the raw enum ordinal
        // instead, "3" being Key.Tab. It matched nothing, silently, which is exactly what a shortcut
        // nobody pressed also looks like.
        return Helpers.WpfUiHelper.MatchesHotkey($"{jumpModifier}+D{digit}", modifiers, Key.D1 + (digit - 1))
            ? digit
            : 0;
    }

    /// <summary>Clicking anything that is not a row drops the selection, as a file manager's own blank
    /// space does.</summary>
    /// <remarks>
    /// Previewed, because the click this exists for lands on a group's ListBox background and that
    /// ListBox would otherwise be first to see it -- and left unhandled, so the row click, the rubber
    /// band and the drag helper all still get their turn.
    /// </remarks>
    private void Content_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Whatever else this press turns out to be -- a row click, the start of a rubber band, a click on
        // nothing -- the list it landed in is the one the user is now working in. Only right-click and
        // double-click used to say so, so a plain left-click (or a band) left the hotkeys acting on
        // whichever list had been double-clicked last.
        if (e.OriginalSource is DependencyObject clicked
            && FindAncestor<System.Windows.Controls.ListBox>(clicked) is { } list)
            _activeList = list;

        // Not while a selection is being extended. Ctrl or Shift on blank space is the start of adding
        // to what is already selected -- a rubber band drawn with Ctrl held, most obviously -- and this
        // runs first, so without the check it would empty the very selection that gesture is extending.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;

        if (e.OriginalSource is DependencyObject source && IsClickOnNothing(source))
            ClearSelection((DependencyObject)sender);
    }

    /// <summary>Hold the jump-to-Nth-result modifier and press 1-9 to switch workspace.</summary>
    /// <remarks>
    /// Checked ahead of the action hotkeys because a bare digit can legitimately be bound to an action,
    /// and the modifier is what tells the two apart.
    /// </remarks>
    private bool TrySwitchWorkspace(System.Windows.Input.KeyEventArgs e)
    {
        var index = WorkspaceIndexFor(e.Key, Keyboard.Modifiers, Core.UserSettings.Load().Hotkeys.SelectJumpModifier);
        if (index == 0 || DataContext is not ViewModels.QuickPanel.QuickPanelViewModel viewModel) return false;

        e.Handled = true;
        _ = viewModel.SelectTabAtAsync(index);
        return true;
    }

    /// <summary>Runs an action's configured hotkey against the selection, without opening the menu.</summary>
    /// <remarks>
    /// The full window reaches this through SearchInputHelper.TryActionHotkey, which needs an
    /// ISearchWindow and a ShellMenuPresenter for gates this panel has no equivalent of: whether the
    /// search caret is at the end, and whether an actions pane could be shown. The execution underneath
    /// is an overload taking only IPluginSearchWindow, which is what the quick navigation menu already
    /// calls for the same reason, so the panel uses that directly.
    ///
    /// Bare keys are allowed through here where the full window checks its caret first -- but only while
    /// nothing is being typed into. That used to be free: there was no box in this panel at all, so a
    /// bound key could only have been meant as the action. The filter box changed that premise, and
    /// every combination stands down while it has focus, not just the bare ones: Ctrl+A and Ctrl+C in a
    /// text box are the box's, whatever else they may also be bound to.
    /// </remarks>
    private void TryRunActionHotkey(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        if (Keyboard.Modifiers == ModifierKeys.None && !Helpers.HotkeyActionTrigger.HasBareKeyActionHotkey(e.Key))
            return;

        var selection = _activeList?.SelectedItems.OfType<AppSearchResult>().ToList() ?? new List<AppSearchResult>();
        if (selection.Count == 0) return;

        if (Helpers.HotkeyActionTrigger.TryExecute(e, selection, this, PluginSdk.Abstractions.SearchWindowType.Main, hideOnRun: true))
            e.Handled = true;
    }

    /// <summary>Whether what was hit is blank space rather than something that acts on its own.</summary>
    /// <remarks>
    /// A row, obviously. Also anything in a group header: collapsing a group or switching its order is
    /// not a click on nothing, and taking the selection away with it would be a surprise.
    /// </remarks>
    internal static bool IsClickOnNothing(DependencyObject source)
        => FindAncestor<System.Windows.Controls.ListBoxItem>(source) == null
           && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) == null;

    // Through TreeWalk rather than VisualTreeHelper directly: what a click starts on is often not a
    // Visual at all -- see there.
    private static T? FindAncestor<T>(DependencyObject from) where T : DependencyObject
        => Helpers.Visuals.TreeWalk.Ancestor<T>(from);

    /// <summary>Empties every group's list under the given root.</summary>
    /// <remarks>
    /// Every one, not just the one under the pointer: each group renders its own list, so a selection
    /// left in another would keep a row looking selected that a keystroke no longer acts on.
    /// </remarks>
    internal static void ClearSelection(DependencyObject root)
    {
        if (root is System.Windows.Controls.ListBox list)
        {
            list.UnselectAll();
            return;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ClearSelection(VisualTreeHelper.GetChild(root, i));
    }

    /// <summary>How much one Ctrl+wheel notch moves the tile scale.</summary>
    /// <remarks>
    /// A tenth per notch puts the whole 20%-to-100% range eight notches wide: coarse enough to cross it in
    /// one flick, fine enough that a single notch is a visible step rather than a jump. The scale is
    /// stored rounded to this same step so a value typed into the settings file still lands on a notch.
    /// </remarks>
    internal const double IconScaleStep = 0.1;

    /// <summary>
    /// The scale a wheel notch asks for: up for a forward roll, down for a backward one.
    /// </summary>
    /// <remarks>
    /// This adjusts the multiplier over the tier the settings page chose, and does not touch the tier
    /// itself: the wheel is the gesture for "a little bigger than this", not a second place to pick a
    /// size from. See <see cref="Core.QuickPanelSettings.IconScale"/> for why the two are separate.
    ///
    /// <para>Rounded to the step before and after the move, so repeated notches cannot drift: adding 0.1
    /// eight times in binary floating point otherwise lands just under 1.0 and the last notch does
    /// nothing.</para>
    ///
    /// <para>The result is clamped directly, NOT through <see cref="Core.QuickPanelSettings.ClampIconScale"/>.
    /// That method reads a value of zero or less as "nothing was ever stored" and answers the default,
    /// which is right for a settings file read at startup and wrong here: one notch below the floor
    /// rounds the step count to zero, and passing that through it made the smallest size jump back to the
    /// default. A notch that would go past an end simply stays on it.</para>
    /// </remarks>
    internal static double ScaledIconScale(double current, int wheelDelta)
    {
        if (wheelDelta == 0) return Core.QuickPanelSettings.ClampIconScale(current);

        var steps = Math.Round(Core.QuickPanelSettings.ClampIconScale(current) / IconScaleStep);
        steps += wheelDelta > 0 ? 1 : -1;

        var moved = Math.Round(steps * IconScaleStep, 2);
        return Math.Clamp(moved, Core.QuickPanelSettings.MinIconScale, Core.QuickPanelSettings.DefaultIconScale);
    }

    /// <summary>Turns the tile scale up or down, and re-measures the panel around it.</summary>
    /// <remarks>
    /// Only while Ctrl is held, so a plain wheel still scrolls the groups -- that is the gesture the panel
    /// is for, and it would be the one thing lost by claiming the wheel outright.
    ///
    /// Saved and applied immediately rather than on close: the panel is closed by losing the foreground as
    /// often as by Escape, and a size that came back wrong on the next summon would read as broken. The
    /// metrics read the multiplier during measure, so writing it and asking for a new layout is the whole
    /// of the update -- nothing is reloaded, nothing re-fetched.
    ///
    /// This is the panel's own tile scale, deliberately not <c>UiMetrics</c>'s: that one is the app-wide
    /// display scale every window shares, and turning it from here would resize the search windows too.
    /// </remarks>
    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        var settings = Core.UserSettings.Load();
        var scale = ScaledIconScale(settings.QuickPanel.EffectiveIconScale, e.Delta);

        // Handled either way, including at the ends of the range, so a roll against the stop does not fall
        // through and scroll the groups the user was not asking to move.
        e.Handled = true;
        if (Math.Abs(scale - settings.QuickPanel.EffectiveIconScale) < 0.0001) return;

        settings.QuickPanel.IconScale = scale;
        settings.Save();

        ApplyIconScale();
    }

    /// <summary>Lays the panel out again at the scale the settings now hold.</summary>
    /// <remarks>
    /// Called after any layout that could have built new rows too, since a group materialized later is
    /// measured at whatever the scale is by then.
    /// </remarks>
    internal void ApplyIconScale()
    {
        InvalidateMeasure();
        UpdateLayout();
    }
}
