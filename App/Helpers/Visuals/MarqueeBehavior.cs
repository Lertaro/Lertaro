using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Attached behavior that automatically scrolls TextBlock content when it overflows
/// and its parent ListBoxItem is selected or hovered.
/// </summary>
public static class MarqueeBehavior
{
    public static readonly DependencyProperty EnableMarqueeProperty =
        DependencyProperty.RegisterAttached("EnableMarquee", typeof(bool), typeof(MarqueeBehavior),
            new PropertyMetadata(false, OnEnableMarqueeChanged));

    public static bool GetEnableMarquee(DependencyObject obj) => (bool)obj.GetValue(EnableMarqueeProperty);
    public static void SetEnableMarquee(DependencyObject obj, bool value) => obj.SetValue(EnableMarqueeProperty, value);

    private static void OnEnableMarqueeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.Loaded -= Element_Loaded;
        element.Unloaded -= Element_Unloaded;

        if ((bool)e.NewValue)
        {
            element.Loaded += Element_Loaded;
            element.Unloaded += Element_Unloaded;
            if (element.IsLoaded)
            {
                InitializeMarquee(element);
            }
        }
        else
        {
            CleanupMarquee(element);
        }
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            InitializeMarquee(element);
        }
    }

    private static void Element_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            CleanupMarquee(element);
        }
    }

    private static void InitializeMarquee(FrameworkElement element)
    {
        CleanupMarquee(element);

        // No containing ListBoxItem/MenuItem (e.g. a standalone header banner rather than a list row):
        // there's no hover/select gesture to reserve the effect for, so it just animates whenever it
        // overflows. A row inside a list or a cascading menu still gates on its own container's
        // hover/select/highlight state so every overflowing row doesn't scroll at once.
        var listBoxItem = FindVisualAncestor<ListBoxItem>(element);
        var menuItem = listBoxItem == null ? FindVisualAncestor<MenuItem>(element) : null;
        Func<bool> isActive = listBoxItem != null
            ? () => listBoxItem.IsMouseOver || listBoxItem.IsSelected
            : menuItem != null
                ? () => menuItem.IsHighlighted
                : () => true;

        if (element.RenderTransform is not TranslateTransform)
        {
            element.RenderTransform = new TranslateTransform();
        }

        DependencyPropertyDescriptor? isMouseOverDescriptor = null;
        DependencyPropertyDescriptor? isSelectedDescriptor = null;
        DependencyPropertyDescriptor? isHighlightedDescriptor = null;
        var watchedContainer = (DependencyObject?)listBoxItem ?? menuItem;
        EventHandler? handler = null;

        if (listBoxItem != null)
        {
            isMouseOverDescriptor = DependencyPropertyDescriptor.FromProperty(UIElement.IsMouseOverProperty, typeof(ListBoxItem));
            isSelectedDescriptor = DependencyPropertyDescriptor.FromProperty(ListBoxItem.IsSelectedProperty, typeof(ListBoxItem));

            handler = (s, e) => UpdateMarqueeAnimation(element, isActive);

            isMouseOverDescriptor?.AddValueChanged(listBoxItem, handler);
            isSelectedDescriptor?.AddValueChanged(listBoxItem, handler);
        }
        else if (menuItem != null)
        {
            isHighlightedDescriptor = DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem));

            handler = (s, e) => UpdateMarqueeAnimation(element, isActive);

            isHighlightedDescriptor?.AddValueChanged(menuItem, handler);
        }

        element.SizeChanged += (s, e) => UpdateMarqueeAnimation(element, isActive);

        if (VisualTreeHelper.GetParent(element) is FrameworkElement parent)
        {
            parent.SizeChanged += (s, e) => UpdateMarqueeAnimation(element, isActive);
        }

        var state = new MarqueeState
        {
            WatchedContainer = watchedContainer,
            IsMouseOverDescriptor = isMouseOverDescriptor,
            IsSelectedDescriptor = isSelectedDescriptor,
            IsHighlightedDescriptor = isHighlightedDescriptor,
            Handler = handler
        };
        SetMarqueeState(element, state);

        UpdateMarqueeAnimation(element, isActive);
    }

    private static void CleanupMarquee(FrameworkElement element)
    {
        var state = GetMarqueeState(element);
        if (state != null)
        {
            if (state.WatchedContainer != null && state.Handler != null)
            {
                state.IsMouseOverDescriptor?.RemoveValueChanged(state.WatchedContainer, state.Handler);
                state.IsSelectedDescriptor?.RemoveValueChanged(state.WatchedContainer, state.Handler);
                state.IsHighlightedDescriptor?.RemoveValueChanged(state.WatchedContainer, state.Handler);
            }
            SetMarqueeState(element, null);
        }

        if (element.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }
    }

    private static void UpdateMarqueeAnimation(FrameworkElement element, Func<bool> isActive)
    {
        if (element.RenderTransform is not TranslateTransform translate) return;

        if (VisualTreeHelper.GetParent(element) is not FrameworkElement parent) return;

        var availableWidth = parent.ActualWidth;
        var elementWidth = element.ActualWidth;

        if (availableWidth <= 0 || elementWidth <= 0) return;

        var overflow = elementWidth - availableWidth;
        var shouldAnimate = overflow > 0 && isActive();

        if (shouldAnimate)
        {
            var speed = 40.0; // pixels per second
            var durationSeconds = overflow / speed;

            var keyFrameAnimation = new DoubleAnimationUsingKeyFrames
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = true
            };

            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8 + durationSeconds))));
            keyFrameAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8 + durationSeconds + 1.0))));

            translate.BeginAnimation(TranslateTransform.XProperty, keyFrameAnimation);
        }
        else
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.X = 0;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T ancestor) return ancestor;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static readonly DependencyProperty MarqueeStateProperty =
        DependencyProperty.RegisterAttached("MarqueeState", typeof(MarqueeState), typeof(MarqueeBehavior), new PropertyMetadata(null));

    private static MarqueeState? GetMarqueeState(DependencyObject obj) => (MarqueeState?)obj.GetValue(MarqueeStateProperty);
    private static void SetMarqueeState(DependencyObject obj, MarqueeState? value) => obj.SetValue(MarqueeStateProperty, value);

    private class MarqueeState
    {
        public DependencyObject? WatchedContainer { get; set; }
        public DependencyPropertyDescriptor? IsMouseOverDescriptor { get; set; }
        public DependencyPropertyDescriptor? IsSelectedDescriptor { get; set; }
        public DependencyPropertyDescriptor? IsHighlightedDescriptor { get; set; }
        public EventHandler? Handler { get; set; }
    }
}
