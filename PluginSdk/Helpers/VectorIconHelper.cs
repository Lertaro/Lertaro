using System.Windows.Media;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Helper class to create WPF ImageSource from vector SVG path data.
/// Shared with plugins.
/// </summary>
public static class VectorIconHelper
{
    /// <summary>Creates a frozen DrawingImage from an SVG path data string and hex color.</summary>
    public static ImageSource CreateVectorIcon(string pathData, string colorHexOrKey)
    {
        var geometry = Geometry.Parse(pathData);
        var group = new DrawingGroup();

        Brush? brush = null;
        if (!string.IsNullOrEmpty(colorHexOrKey))
        {
            brush = System.Windows.Application.Current?.TryFindResource(colorHexOrKey) as Brush;
            if (brush == null)
            {
                try
                {
                    brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHexOrKey));
                }
                catch
                {
                    // Fallback if not a valid hex and not found in resources
                }
            }
        }
        if (brush == null)
        {
            brush = System.Windows.Application.Current?.TryFindResource("TextPrimary") as Brush
                    ?? Brushes.Gray;
        }

        group.Children.Add(new GeometryDrawing(brush, null, geometry));
        var image = new DrawingImage(group);
        try
        {
            image.Freeze();
        }
        catch { }
        return image;
    }
}
