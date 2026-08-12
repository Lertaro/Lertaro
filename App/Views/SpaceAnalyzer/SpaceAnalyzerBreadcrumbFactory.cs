using System.Windows;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace Lertaro.App.Views.SpaceAnalyzer;

/// <summary>
/// Split out solely to keep the analyzer view under the repository's per-file line limit; it creates
/// the stateless breadcrumb separator used between location buttons.
/// </summary>
internal static class SpaceAnalyzerBreadcrumbFactory
{
    public static TextBlock Create(FrameworkElement resourceOwner) => new()
    {
        Text = "\uE76C",
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 9,
        Foreground = (Brush)resourceOwner.FindResource("TextSecondary"),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(1, 0, 1, 0)
    };
}
