using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Border.ClipToBounds clips children to the element's plain rectangular layout bounds -- it never
/// respects CornerRadius, a long-standing WPF gap (Border paints its own Background/BorderBrush
/// rounded, but does nothing to round how it clips its child). Any child that paints a background
/// flush to the edge (e.g. a themed ContentBg row) shows a small square flap poking past the outer
/// chrome's rounded arc at each corner. This attaches a real rounded-rect Clip instead, tracking
/// both size and CornerRadius changes since CornerRadius can be set dynamically at runtime (e.g.
/// per docking mode in InlineSearchWindowPositioner/SearchWindowChromeHandler).
/// </summary>
public static class RoundedClip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(RoundedClip), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(Border border, bool value) => border.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(Border border) => (bool)border.GetValue(IsEnabledProperty);

    // Per-border subscription, keyed weakly: the value closes over the border (through Update), so a
    // strongly-keyed table here would root every border it had ever seen.
    private static readonly ConditionalWeakTable<Border, Subscription> Subscriptions = new();

    private sealed record Subscription(DependencyPropertyDescriptor Descriptor, EventHandler Handler, Action Update);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border) return;

        border.Loaded -= OnLoaded;
        border.Unloaded -= OnUnloaded;

        if (e.NewValue is true)
        {
            border.Loaded += OnLoaded;
            border.Unloaded += OnUnloaded;
            if (border.IsLoaded)
                Attach(border);
        }
        else
        {
            Detach(border);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
            Attach(border);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Border border)
            Detach(border);
    }

    private static void Attach(Border border)
    {
        Detach(border);

        void Update() => border.Clip = BuildGeometry(border.RenderSize, border.CornerRadius);
        EventHandler handler = (_, _) => Update();

        // AddValueChanged registers this Border in a PROCESS-WIDE static table
        // (Type._attachedPropertyBrowsableType). RemoveValueChanged is the only thing that takes it back
        // out again, so leaving it registered roots the Border -- and through it the whole window visual
        // tree -- until the process exits. That is exactly what happened here: every closed inline window
        // stayed alive (measured 17 of them, with a weak-event table grown to 21MB), and each one made
        // every later UI-thread event dispatch a little slower. Hence the detach on Unloaded below.
        var descriptor = DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border));
        descriptor.AddValueChanged(border, handler);

        Subscriptions.AddOrUpdate(border, new Subscription(descriptor, handler, Update));
        border.SizeChanged += OnSizeChanged;
        Update();
    }

    private static void Detach(Border border)
    {
        border.SizeChanged -= OnSizeChanged;

        if (Subscriptions.TryGetValue(border, out var subscription))
        {
            subscription.Descriptor.RemoveValueChanged(border, subscription.Handler);
            Subscriptions.Remove(border);
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border border && Subscriptions.TryGetValue(border, out var subscription))
            subscription.Update();
    }

    private static Geometry BuildGeometry(Size size, CornerRadius r)
    {
        if (size.Width <= 0 || size.Height <= 0) return Geometry.Empty;

        var rect = new Rect(size);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left + r.TopLeft, rect.Top), true, true);
            ctx.LineTo(new Point(rect.Right - r.TopRight, rect.Top), true, false);
            if (r.TopRight > 0) ctx.ArcTo(new Point(rect.Right, rect.Top + r.TopRight), new Size(r.TopRight, r.TopRight), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(rect.Right, rect.Bottom - r.BottomRight), true, false);
            if (r.BottomRight > 0) ctx.ArcTo(new Point(rect.Right - r.BottomRight, rect.Bottom), new Size(r.BottomRight, r.BottomRight), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(rect.Left + r.BottomLeft, rect.Bottom), true, false);
            if (r.BottomLeft > 0) ctx.ArcTo(new Point(rect.Left, rect.Bottom - r.BottomLeft), new Size(r.BottomLeft, r.BottomLeft), 0, false, SweepDirection.Clockwise, true, false);
            ctx.LineTo(new Point(rect.Left, rect.Top + r.TopLeft), true, false);
            if (r.TopLeft > 0) ctx.ArcTo(new Point(rect.Left + r.TopLeft, rect.Top), new Size(r.TopLeft, r.TopLeft), 0, false, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }
}
