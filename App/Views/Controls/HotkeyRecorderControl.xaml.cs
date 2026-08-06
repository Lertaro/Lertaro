using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers;

namespace Lertaro.App.Views.Controls;

public partial class HotkeyRecorderControl : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(HotkeyRecorderControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyRecorderControl ctrl)
            ctrl.HotkeyBox.Text = Core.HotkeyStringFormat.ToDisplayText(e.NewValue as string ?? string.Empty);
    }

    private static object CoerceValue(DependencyObject _, object baseValue) =>
        baseValue is string value && Core.HotkeyStringFormat.IsReservedWindowsShortcut(value) ? string.Empty : baseValue;

    public static readonly DependencyProperty RequireModifierProperty =
        DependencyProperty.Register(nameof(RequireModifier), typeof(bool),
            typeof(HotkeyRecorderControl), new PropertyMetadata(false));

    public bool RequireModifier
    {
        get => (bool)GetValue(RequireModifierProperty);
        set => SetValue(RequireModifierProperty, value);
    }

    // When true, only the held modifier is recorded (the triggering key itself is discarded) -- used
    // for the "jump to result N" row, where the digit is fixed and only the modifier is configurable.
    public static readonly DependencyProperty ModifierOnlyProperty =
        DependencyProperty.Register(nameof(ModifierOnly), typeof(bool),
            typeof(HotkeyRecorderControl), new PropertyMetadata(false));

    public bool ModifierOnly
    {
        get => (bool)GetValue(ModifierOnlyProperty);
        set => SetValue(ModifierOnlyProperty, value);
    }

    // Shown by the restore button once the value has been cleared, so a disabled shortcut can be
    // put back the way it was without the user having to remember/retype the original combo.
    public static readonly DependencyProperty DefaultValueProperty =
        DependencyProperty.Register(nameof(DefaultValue), typeof(string),
            typeof(HotkeyRecorderControl), new PropertyMetadata(string.Empty));

    public string DefaultValue
    {
        get => (string)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    // When true, a bare modifier released alone is kept as a real, complete value (e.g. "Ctrl") instead
    // of being treated as a clear -- for fields where a bare modifier has its own separate runtime
    // meaning (e.g. the toggle-window hotkey's "double-tap this modifier" mode).
    public static readonly DependencyProperty AllowBareModifierProperty =
        DependencyProperty.Register(nameof(AllowBareModifier), typeof(bool),
            typeof(HotkeyRecorderControl), new PropertyMetadata(false));

    public bool AllowBareModifier
    {
        get => (bool)GetValue(AllowBareModifierProperty);
        set => SetValue(AllowBareModifierProperty, value);
    }

    // Whether a real (non-modifier) key was pressed since a modifier was last held down. Decided on
    // KeyUp rather than a modifier's own KeyDown: WPF's routing for a *bare* modifier press (no other
    // key involved) is unreliable to key off of directly, but its KeyUp always arrives, so recording
    // is deferred to release -- if nothing else was pressed in between, the modifier alone is the value.
    private bool _realKeyPressedDuringModifierHold;
    private ModifierKeys _trackedModifiers;

    public HotkeyRecorderControl() => InitializeComponent();

    private void ClearButton_Click(object sender, RoutedEventArgs e) => Value = string.Empty;

    private void RestoreButton_Click(object sender, RoutedEventArgs e) => Value = DefaultValue;

    private static string? ModifierName(Key key) => HotkeyRecorderModifierState.FromKey(key) switch
    {
        ModifierKeys.Control => "Ctrl",
        ModifierKeys.Alt => "Alt",
        ModifierKeys.Shift => "Shift",
        ModifierKeys.Windows => "Win",
        _ => null
    };

    // WPF's Key enum's canonical name for this key is "Return", but every hardcoded plugin hotkey
    // string (and everywhere else in this app) spells it "Enter" -- normalize so recorded values
    // match instead of showing "Alt+Return" next to a plugin default of "Ctrl+Shift+Enter".
    private static string KeyDisplayName(Key key) => key == Key.Return ? "Enter" : key.ToString();

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = WpfUiHelper.GetActualKey(e);

        if (key == Key.Escape) { Value = string.Empty; return; }
        if (key is Key.Clear or Key.OemClear) return;

        if (HotkeyRecorderModifierState.FromKey(key) != ModifierKeys.None)
        {
            _trackedModifiers = HotkeyRecorderModifierState.Add(_trackedModifiers, key);
            // Wait for KeyUp to decide: if nothing else is pressed before it's released, the bare
            // modifier itself is the recorded value (see HotkeyBox_PreviewKeyUp).
            return;
        }

        _realKeyPressedDuringModifierHold = true;

        var modifiers = HotkeyRecorderModifierState.Combine(Keyboard.Modifiers, _trackedModifiers);
        if (e.Key == Key.System) modifiers |= ModifierKeys.Alt;

        if (ModifierOnly)
        {
            // Only one modifier makes sense here (it pairs with a fixed digit 1-9 elsewhere), so pick
            // a single dominant one rather than recording a compound "Ctrl+Shift" that nothing parses.
            // Pressing a real key with no modifier held isn't a single-modifier value at all -- clear
            // instead of showing a "None" placeholder value.
            Value = modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl"
                : modifiers.HasFlag(ModifierKeys.Alt) ? "Alt"
                : modifiers.HasFlag(ModifierKeys.Shift) ? "Shift"
                : modifiers.HasFlag(ModifierKeys.Windows) ? "Win"
                : string.Empty;
            return;
        }

        if (RequireModifier && modifiers == ModifierKeys.None) return;

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyDisplayName(key));
        Value = string.Join("+", parts);
    }

    private void HotkeyBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = WpfUiHelper.GetActualKey(e);

        var releasedModifier = ModifierName(key);
        if (releasedModifier == null) return;

        if (!_realKeyPressedDuringModifierHold)
        {
            // Released alone -- nothing else was pressed while it was held. For ModifierOnly (matched
            // by comparing held-modifier state, no key needed) or AllowBareModifier (a field with its
            // own separate meaning for a bare modifier) this is a real, working value. A plain combo
            // recorder has no key to pair it with, so runtime matching (which needs both a modifier
            // AND a key) would never fire -- treat it as a clear instead of saving a value that looks
            // configured but silently does nothing.
            Value = (ModifierOnly || AllowBareModifier) ? releasedModifier : string.Empty;
        }

        _trackedModifiers = HotkeyRecorderModifierState.Remove(_trackedModifiers, key);
        if (_trackedModifiers == ModifierKeys.None)
            _realKeyPressedDuringModifierHold = false;
    }
}
