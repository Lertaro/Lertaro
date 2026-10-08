using System.Windows;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace Lertaro.App.Services.Theme;

/// <summary>Uses the fixed blue brand artwork for native and in-window title-bar icons.</summary>
public static class ThemedWindowIconHelper
{
    private static readonly Lazy<BitmapImage> Icon = new(() =>
    {
        var bitmap = new BitmapImage(new Uri("pack://application:,,,/Lertaro.App;component/tray.png", UriKind.Absolute));
        bitmap.Freeze();
        return bitmap;
    });

    public static void Apply(Window window) => window.Icon = Icon.Value;

    public static void Apply(Image image, Window window) => image.Source = Icon.Value;
}
