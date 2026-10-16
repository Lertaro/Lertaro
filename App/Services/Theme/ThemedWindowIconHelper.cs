using System.Windows;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace Lertaro.App.Services.Theme;

/// <summary>
/// Hands out the two forms of the brand mark: the native window icon stays a size-specific ICO
/// frame because Win32 only accepts an HICON, while in-app brand images use the theme-tinted
/// vector from <see cref="BrandLogo"/>.
/// </summary>
public static class ThemedWindowIconHelper
{
    private static readonly Lazy<BitmapFrame[]> Frames = new(() =>
    {
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/Lertaro.App;component/logo.ico", UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frames = decoder.Frames.OrderBy(frame => frame.PixelWidth).ToArray();
        foreach (var frame in frames) frame.Freeze();
        return frames;
    });

    private static readonly List<WeakReference<Image>> BrandMarks = [];
    private static readonly object Gate = new();

    // Keep the decoder on the BitmapFrame: WPF can select native small/large window icons from it.
    public static void Apply(Window window) => window.Icon = GetFrame(32);

    public static void Apply(Image image)
    {
        image.Source = BrandLogo.Create();
        lock (Gate) BrandMarks.Add(new WeakReference<Image>(image));
    }

    /// <summary>Re-paints every brand image handed out so far, because a frozen vector mark is a
    /// snapshot of the accent it was built under. Wired to ThemeChanged during app startup.</summary>
    public static void RepaintBrandMarks()
    {
        var source = BrandLogo.Create();
        var live = new List<Image>();
        lock (Gate)
        {
            BrandMarks.RemoveAll(weak => !weak.TryGetTarget(out _));
            foreach (var weak in BrandMarks)
                if (weak.TryGetTarget(out var image)) live.Add(image);
        }
        foreach (var image in live)
        {
            if (image.CheckAccess()) image.Source = source;
            else image.Dispatcher.BeginInvoke(() => image.Source = source);
        }
    }

    public static readonly DependencyProperty UseAppIconProperty = DependencyProperty.RegisterAttached(
        "UseAppIcon", typeof(bool), typeof(ThemedWindowIconHelper),
        new PropertyMetadata(false, (sender, e) =>
        {
            if (sender is Image image && (bool)e.NewValue) Apply(image);
        }));

    public static bool GetUseAppIcon(Image image) => (bool)image.GetValue(UseAppIconProperty);
    public static void SetUseAppIcon(Image image, bool value) => image.SetValue(UseAppIconProperty, value);

    internal static BitmapFrame GetFrame(double pixelSize) =>
        Frames.Value.FirstOrDefault(frame => frame.PixelWidth >= pixelSize) ?? Frames.Value[^1];
}
