using System.ComponentModel;
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

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border border || e.NewValue is not true) return;

        border.SizeChanged += (_, _) => Update(border);
        DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border))
            .AddValueChanged(border, (_, _) => Update(border));
        Update(border);
    }

    private static void Update(Border border) => border.Clip = BuildGeometry(border.RenderSize, border.CornerRadius);

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
