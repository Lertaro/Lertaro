using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lertaro.PluginSdk.Windows;

internal static class PluginWindowClip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PluginWindowClip), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Border border) return;
        if ((bool)e.NewValue)
        {
            border.SizeChanged += Border_SizeChanged;
            UpdateClip(border);
        }
        else
        {
            border.SizeChanged -= Border_SizeChanged;
            border.Clip = null;
        }
    }

    private static void Border_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip((Border)sender);

    private static void UpdateClip(Border border)
    {
        if (border.ActualWidth <= 0 || border.ActualHeight <= 0) return;
        var radius = border.CornerRadius.TopLeft;
        border.Clip = new RectangleGeometry(new Rect(0, 0, border.ActualWidth, border.ActualHeight), radius, radius);
    }
}
