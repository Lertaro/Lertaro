using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services;
using Brush = System.Windows.Media.Brush;

namespace Lertaro.App.Views.SpaceAnalyzer;

/// <summary>
/// Creates separators between the space analyzer's breadcrumb buttons.
/// </summary>
internal static class SpaceAnalyzerBreadcrumbFactory
{
    public static TextBlock Create(FrameworkElement resourceOwner) => new()
    {
        Text = "\uE76C",
        FontFamily = AppTypography.IconFont,
        FontSize = 9,
        Foreground = (Brush)resourceOwner.FindResource("TextSecondary"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(1, 0, 1, 0)
    };
}
