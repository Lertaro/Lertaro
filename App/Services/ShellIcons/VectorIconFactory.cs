using System.Windows.Media;

namespace Lertaro.App.Services.ShellIcons;

/// <summary>
/// Builds vector (geometry) based icons used for plugin actions and the built-in
/// "show more" result. Kept separate from shell icon extraction to stay modular.
/// </summary>
internal static class VectorIconFactory
{
    public static ImageSource ShowMore()
    {
        var geometry = Geometry.Parse("M14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z");
        var group = new DrawingGroup();
        var brush = System.Windows.Application.Current?.TryFindResource("AccentBlue") as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.Blue;
        group.Children.Add(new GeometryDrawing(brush, null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    public static ImageSource Create(string pathData, string colorHexOrKey)
    {
        var geometry = Geometry.Parse(pathData);
        var group = new DrawingGroup();
        var brush = System.Windows.Application.Current?.TryFindResource(colorHexOrKey) as System.Windows.Media.Brush;
        if (brush == null && !string.IsNullOrEmpty(colorHexOrKey))
        {
            try { brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHexOrKey)); }
            catch { }
        }
        brush ??= System.Windows.Application.Current?.TryFindResource("TextPrimary") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
        group.Children.Add(new GeometryDrawing(brush, null, geometry));
        var image = new DrawingImage(group);
        try { image.Freeze(); } catch { }
        return image;
    }
}
