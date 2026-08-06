using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;

namespace Lertaro.App.Services.Theme;

/// <summary>
/// Sets a window's title-bar icon (native Window.Icon, and/or an in-window logo Image) to a
/// monochrome, theme-colored render of tray.png -- the same silhouette source TrayIconService
/// recolors for the system tray icon, just recolored here as a plain WPF BitmapSource (both
/// Window.Icon and Image.Source accept any ImageSource, so no .ico conversion is needed).
/// Re-renders whenever the active theme changes.
/// </summary>
public static class ThemedWindowIconHelper
{
    private static readonly Uri SourceUri = new("pack://application:,,,/Lertaro.App;component/tray.png", UriKind.Absolute);

    public static void Apply(Window window) => ApplyCore(bmp => window.Icon = bmp, window);

    public static void Apply(Image image, Window window) => ApplyCore(bmp => image.Source = bmp, window);

    private static void ApplyCore(Action<BitmapSource> setIcon, Window window)
    {
        void Update() => setIcon(Render());
        Update();

        void OnThemeChanged() => window.Dispatcher.Invoke(Update);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        window.Closed += (_, _) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
    }

    private static BitmapSource Render()
    {
        Color color;
        if (ThemeManager.Instance.ActiveTheme?.IsDark == true)
        {
            color = Colors.White;
        }
        else
        {
            var brush = Application.Current.Resources["AccentBlue"] as SolidColorBrush;
            color = brush?.Color ?? Colors.DodgerBlue;
        }

        var source = new BitmapImage(SourceUri);
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        // Bgra32 is straight (non-premultiplied) alpha, stored B,G,R,A per pixel -- replace the color
        // channels with the theme color while keeping the source alpha, i.e. its silhouette shape.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
        }

        var bitmap = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, converted.DpiX, converted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
