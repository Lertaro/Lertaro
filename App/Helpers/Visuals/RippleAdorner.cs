using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lertaro.App.Helpers.Visuals;

public class RippleAdorner : Adorner
{
    private readonly System.Windows.Point _center;

    public static readonly DependencyProperty CurrentRadiusProperty =
        DependencyProperty.Register(nameof(CurrentRadius), typeof(double), typeof(RippleAdorner),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double CurrentRadius
    {
        get => (double)GetValue(CurrentRadiusProperty);
        set => SetValue(CurrentRadiusProperty, value);
    }

    public static readonly DependencyProperty CurrentOpacityProperty =
        DependencyProperty.Register(nameof(CurrentOpacity), typeof(double), typeof(RippleAdorner),
            new FrameworkPropertyMetadata(0.4, FrameworkPropertyMetadataOptions.AffectsRender));

    public double CurrentOpacity
    {
        get => (double)GetValue(CurrentOpacityProperty);
        set => SetValue(CurrentOpacityProperty, value);
    }

    public RippleAdorner(UIElement adornedElement, System.Windows.Point center) : base(adornedElement)
    {
        _center = center;
        IsHitTestVisible = false;

        var rippleDuration = System.Windows.Application.Current?.TryFindResource("DurationRipple") is Duration d
            ? d.TimeSpan
            : TimeSpan.FromMilliseconds(400);

        // Animate Radius
        var targetRadius = Math.Max(adornedElement.RenderSize.Width, adornedElement.RenderSize.Height) * 1.5;
        var radiusAnimation = new DoubleAnimation(0.0, targetRadius, rippleDuration)
        {
            EasingFunction = System.Windows.Application.Current?.TryFindResource("EaseOutCubic") as IEasingFunction
                              ?? new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // Animate Opacity
        var opacityAnimation = new DoubleAnimation(0.4, 0.0, rippleDuration)
        {
            EasingFunction = System.Windows.Application.Current?.TryFindResource("EaseOutExponential") as IEasingFunction
                              ?? new ExponentialEase { EasingMode = EasingMode.EaseOut }
        };

        opacityAnimation.Completed += (s, e) =>
        {
            var layer = AdornerLayer.GetAdornerLayer(adornedElement);
            layer?.Remove(this);
        };

        BeginAnimation(CurrentRadiusProperty, radiusAnimation);
        BeginAnimation(CurrentOpacityProperty, opacityAnimation);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var brushColor = System.Windows.Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush
                         ?? System.Windows.Media.Brushes.LightBlue;

        var rippleBrush = new SolidColorBrush(brushColor.Color)
        {
            Opacity = CurrentOpacity
        };
        rippleBrush.Freeze();

        drawingContext.PushClip(new RectangleGeometry(new Rect(new System.Windows.Point(0, 0), AdornedElement.RenderSize)));
        drawingContext.DrawEllipse(rippleBrush, null, _center, CurrentRadius, CurrentRadius);
        drawingContext.Pop();
    }

    public static readonly DependencyProperty EnableRippleProperty =
        DependencyProperty.RegisterAttached("EnableRipple", typeof(bool), typeof(RippleAdorner),
            new PropertyMetadata(false, OnEnableRippleChanged));

    public static bool GetEnableRipple(DependencyObject obj) => (bool)obj.GetValue(EnableRippleProperty);
    public static void SetEnableRipple(DependencyObject obj, bool value) => obj.SetValue(EnableRippleProperty, value);

    private static void OnEnableRippleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && e.NewValue is bool enabled)
        {
            if (enabled)
            {
                element.PreviewMouseLeftButtonDown += Element_PreviewMouseLeftButtonDown;
            }
            else
            {
                element.PreviewMouseLeftButtonDown -= Element_PreviewMouseLeftButtonDown;
            }
        }
    }

    private static void Element_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement clickedElement)
        {
            var layer = AdornerLayer.GetAdornerLayer(clickedElement);
            if (layer != null)
            {
                var center = e.GetPosition(clickedElement);
                var adorner = new RippleAdorner(clickedElement, center);
                layer.Add(adorner);
            }
        }
    }
}
