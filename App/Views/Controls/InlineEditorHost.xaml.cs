using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.Controls;

// A reusable parent-sized editor: it owns overflow detection and click-away commit, while the
// caller supplies the normal content, editor template, and any field-specific validation.
public partial class InlineEditorHost : UserControl
{
    private static readonly DependencyProperty PendingProperty = DependencyProperty.RegisterAttached(
        "Pending", typeof(bool), typeof(InlineEditorHost));

    public static readonly DependencyProperty AutoExpandEnabledProperty = DependencyProperty.Register(
        nameof(AutoExpandEnabled), typeof(bool), typeof(InlineEditorHost));

    public static readonly DependencyProperty EditorTemplateProperty = DependencyProperty.Register(
        nameof(EditorTemplate), typeof(DataTemplate), typeof(InlineEditorHost));

    public static readonly DependencyProperty NormalContentProperty = DependencyProperty.Register(
        nameof(NormalContent), typeof(object), typeof(InlineEditorHost));

    public static readonly DependencyProperty EditorContentProperty = DependencyProperty.Register(
        nameof(EditorContent), typeof(object), typeof(InlineEditorHost));

    public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
        nameof(IsEditing), typeof(bool), typeof(InlineEditorHost));

    private Window? _editingWindow;
    private bool _closing;

    public bool AutoExpandEnabled
    {
        get => (bool)GetValue(AutoExpandEnabledProperty);
        set => SetValue(AutoExpandEnabledProperty, value);
    }

    public DataTemplate? EditorTemplate
    {
        get => (DataTemplate?)GetValue(EditorTemplateProperty);
        set => SetValue(EditorTemplateProperty, value);
    }

    public object? NormalContent
    {
        get => GetValue(NormalContentProperty);
        set => SetValue(NormalContentProperty, value);
    }

    public object? EditorContent
    {
        get => GetValue(EditorContentProperty);
        private set => SetValue(EditorContentProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty);
        private set => SetValue(IsEditingProperty, value);
    }

    public event RoutedEventHandler? EditCompleted;

    public InlineEditorHost()
    {
        InitializeComponent();
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
        AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged), true);
        Unloaded += OnUnloaded;
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is TextBox textBox)
            ScheduleExpansion(textBox);
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (e.OriginalSource is TextBox textBox)
            ScheduleExpansion(textBox);
    }

    private void ScheduleExpansion(TextBox textBox)
    {
        if (!AutoExpandEnabled || IsEditing || !textBox.IsKeyboardFocusWithin || textBox.IsReadOnly
            || string.IsNullOrEmpty(textBox.Text) || (bool)textBox.GetValue(PendingProperty))
            return;

        textBox.SetValue(PendingProperty, true);
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            textBox.ClearValue(PendingProperty);
            if (IsLoaded && !IsEditing && textBox.IsKeyboardFocusWithin && IsTextOverflowing(textBox))
                BeginEditing();
        }));
    }

    private void BeginEditing()
    {
        if (IsEditing) return;

        EditorContent = DataContext;
        IsEditing = true;
        _editingWindow = Window.GetWindow(this);
        _editingWindow?.PreviewMouseDown += OnWindowPreviewMouseDown;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsEditing) return;
            var editor = FindVisibleTextBox(this);
            if (editor == null) return;
            editor.Focus();
            editor.CaretIndex = editor.Text.Length;
        }));
    }

    private void OnWindowPreviewMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (_closing || !IsEditing || e.OriginalSource is not DependencyObject source || IsDescendantOf(source, this))
            return;

        CloseEditor();
    }

    private void CloseEditor()
    {
        if (_closing) return;
        _closing = true;
        _editingWindow?.PreviewMouseDown -= OnWindowPreviewMouseDown;
        _editingWindow = null;

        IsEditing = false;
        EditorContent = null;
        _closing = false;
        EditCompleted?.Invoke(this, new RoutedEventArgs());
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (IsEditing)
            CloseEditor();
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        for (var current = child; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;

        return false;
    }

    private static TextBox? FindVisibleTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox textBox && textBox.Visibility == Visibility.Visible)
                return textBox;

            var nested = FindVisibleTextBox(child);
            if (nested != null) return nested;
        }

        return null;
    }

    private static bool IsTextOverflowing(TextBox textBox)
    {
        if (string.IsNullOrEmpty(textBox.Text)) return false;

        var typeface = new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(textBox).PixelsPerDip;
        var lines = textBox.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var maxLineWidth = 0d;
        var totalHeight = 0d;
        foreach (var line in lines)
        {
            var measured = new FormattedText(line.Length == 0 ? " " : line, CultureInfo.CurrentCulture,
                textBox.FlowDirection, typeface, textBox.FontSize, System.Windows.Media.Brushes.Transparent, pixelsPerDip);
            maxLineWidth = Math.Max(maxLineWidth, measured.Width);
            totalHeight += measured.Height;
        }

        var availableWidth = textBox.ActualWidth - textBox.Padding.Left - textBox.Padding.Right
            - textBox.BorderThickness.Left - textBox.BorderThickness.Right;
        var availableHeight = textBox.ActualHeight - textBox.Padding.Top - textBox.Padding.Bottom
            - textBox.BorderThickness.Top - textBox.BorderThickness.Bottom;
        return availableWidth > 0 && availableHeight > 0
            && (maxLineWidth > availableWidth + 1 || totalHeight > availableHeight + 1);
    }
}
