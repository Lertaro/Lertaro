using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Lertaro.App.Converters;

public static class ScrollViewerHelper
{
    public static readonly DependencyProperty ShiftWheelScrollsHorizontallyProperty =
        DependencyProperty.RegisterAttached("ShiftWheelScrollsHorizontally", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnShiftWheelScrollsHorizontallyChanged));

    public static bool GetShiftWheelScrollsHorizontally(DependencyObject obj) => (bool)obj.GetValue(ShiftWheelScrollsHorizontallyProperty);
    public static void SetShiftWheelScrollsHorizontally(DependencyObject obj, bool value) => obj.SetValue(ShiftWheelScrollsHorizontallyProperty, value);

    private static void OnShiftWheelScrollsHorizontallyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        if (Keyboard.Modifiers == ModifierKeys.Shift)
        {
            if (e.Delta > 0)
            {
                scrollViewer.LineLeft();
            }
            else
            {
                scrollViewer.LineRight();
            }
            e.Handled = true;
        }
        else if (scrollViewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            if (VisualTreeHelper.GetParent(scrollViewer) is UIElement parent)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                parent.RaiseEvent(eventArg);
            }
        }
    }

    public static readonly DependencyProperty BubbleMouseWheelProperty =
        DependencyProperty.RegisterAttached("BubbleMouseWheel", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnBubbleMouseWheelChanged));

    public static bool GetBubbleMouseWheel(DependencyObject obj) => (bool)obj.GetValue(BubbleMouseWheelProperty);
    public static void SetBubbleMouseWheel(DependencyObject obj, bool value) => obj.SetValue(BubbleMouseWheelProperty, value);

    private static void OnBubbleMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PreviewMouseWheel -= OnElementPreviewMouseWheel;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnElementPreviewMouseWheel;
        }
    }

    private static void OnElementPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement element) return;

        if (VisualTreeHelper.GetParent(element) is UIElement parent)
        {
            e.Handled = true;
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            parent.RaiseEvent(eventArg);
        }
    }

    // Overlay scrollbar visibility includes the enclosing text control's IsMouseOver. In RichTextBox,
    // pointer events over a Run follow the content tree and skip the ScrollViewer's visual ancestry.
    //
    // Set this read-only property from mouse events and let the ScrollBar style read it. A MultiBinding
    // with TemplatedParent in ControlTemplate.Triggers does not resolve this lookup; PriorityBinding also
    // cannot fall back when FindAncestor returns no value.
    private static readonly DependencyPropertyKey PointerNearPropertyKey =
        DependencyProperty.RegisterAttachedReadOnly("PointerNear", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false));

    public static readonly DependencyProperty PointerNearProperty = PointerNearPropertyKey.DependencyProperty;

    public static bool GetPointerNear(DependencyObject obj) => (bool)obj.GetValue(PointerNearProperty);

    /// <summary>Set by the ScrollViewer style to start tracking <see cref="PointerNearProperty"/>.</summary>
    public static readonly DependencyProperty RevealOnHoverProperty =
        DependencyProperty.RegisterAttached("RevealOnHover", typeof(bool), typeof(ScrollViewerHelper),
            new PropertyMetadata(false, OnRevealOnHoverChanged));

    public static bool GetRevealOnHover(DependencyObject obj) => (bool)obj.GetValue(RevealOnHoverProperty);
    public static void SetRevealOnHover(DependencyObject obj, bool value) => obj.SetValue(RevealOnHoverProperty, value);

    /// <summary>Shows an overlay scrollbar whenever its content needs scrolling instead of only on hover.</summary>
    public static readonly DependencyProperty AlwaysShowScrollBarWhenNeededProperty =
        DependencyProperty.RegisterAttached("AlwaysShowScrollBarWhenNeeded", typeof(bool), typeof(ScrollViewerHelper),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetAlwaysShowScrollBarWhenNeeded(DependencyObject obj) => (bool)obj.GetValue(AlwaysShowScrollBarWhenNeededProperty);
    public static void SetAlwaysShowScrollBarWhenNeeded(DependencyObject obj, bool value) => obj.SetValue(AlwaysShowScrollBarWhenNeededProperty, value);

    // Holds the text control the reveal also listens to, so its handlers can be detached again.
    private static readonly DependencyProperty TextHostProperty =
        DependencyProperty.RegisterAttached("TextHost", typeof(TextBoxBase), typeof(ScrollViewerHelper),
            new PropertyMetadata(null));

    private static void OnRevealOnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        scrollViewer.MouseEnter -= OnRevealPointerChanged;
        scrollViewer.MouseLeave -= OnRevealPointerChanged;
        scrollViewer.Loaded -= OnRevealScrollViewerLoaded;
        DetachTextHost(scrollViewer);

        if (!(bool)e.NewValue) return;

        scrollViewer.MouseEnter += OnRevealPointerChanged;
        scrollViewer.MouseLeave += OnRevealPointerChanged;
        // The ancestor lookup has to wait for the tree: a ScrollViewer applies its style before it is
        // ever attached to the TextBox that will host it.
        scrollViewer.Loaded += OnRevealScrollViewerLoaded;
        if (scrollViewer.IsLoaded)
            OnRevealScrollViewerLoaded(scrollViewer, new RoutedEventArgs());
    }

    private static void OnRevealScrollViewerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;

        // Re-resolved on every load: a ScrollViewer can be unloaded and re-attached elsewhere.
        DetachTextHost(scrollViewer);

        var host = FindTextHost(scrollViewer);
        if (host != null)
        {
            scrollViewer.SetValue(TextHostProperty, host);
            host.MouseEnter += OnRevealPointerChanged;
            host.MouseLeave += OnRevealPointerChanged;
        }

        UpdatePointerNear(scrollViewer);
    }

    private static void DetachTextHost(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(TextHostProperty) is not TextBoxBase host) return;

        host.MouseEnter -= OnRevealPointerChanged;
        host.MouseLeave -= OnRevealPointerChanged;
        scrollViewer.SetValue(TextHostProperty, null);
    }

    // Handles the events of both the ScrollViewer and its text host, so the sender is whichever one the
    // pointer crossed -- the state is recomputed from both either way.
    private static void OnRevealPointerChanged(object sender, MouseEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            UpdatePointerNear(scrollViewer);
            return;
        }
        if (sender is TextBoxBase host && FindDescendantScrollViewer(host) is { } inner)
            UpdatePointerNear(inner);
    }

    private static void UpdatePointerNear(ScrollViewer scrollViewer)
    {
        var host = scrollViewer.GetValue(TextHostProperty) as TextBoxBase;
        scrollViewer.SetValue(PointerNearPropertyKey, ComputePointerNear(scrollViewer.IsMouseOver, host?.IsMouseOver));
    }

    /// <summary>The reveal condition itself: either the ScrollViewer or the text control around it.</summary>
    internal static bool ComputePointerNear(bool scrollViewerHasPointer, bool? textHostHasPointer)
        => scrollViewerHasPointer || textHostHasPointer == true;

    /// <summary>
    /// The text control this ScrollViewer is the content host of, or null when it is an ordinary
    /// standalone ScrollViewer. Stops at the first one: a text box nested inside another control's
    /// ScrollViewer must not make that outer scrollbar reveal.
    /// </summary>
    internal static TextBoxBase? FindTextHost(ScrollViewer scrollViewer)
    {
        for (var node = VisualTreeHelper.GetParent(scrollViewer); node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase host) return host;
            // Anything else that hosts content of its own means this ScrollViewer is not a text box's
            // own scroll host but something further out.
            if (node is ScrollViewer) return null;
        }
        return null;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer hit) return hit;
            if (FindDescendantScrollViewer(child) is { } deeper) return deeper;
        }
        return null;
    }
}
