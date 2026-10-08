using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;

namespace Lertaro.App.Services.Theme;

/// <summary>Uses the ICO's size-specific artwork for native windows and in-app brand images.</summary>
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

    // Keep the decoder on the BitmapFrame: WPF can select native small/large window icons from it.
    public static void Apply(Window window) => window.Icon = GetFrame(32);

    public static void Apply(Image image, Window window) => SetUseAppIcon(image, true);

    public static readonly DependencyProperty UseAppIconProperty = DependencyProperty.RegisterAttached(
        "UseAppIcon", typeof(bool), typeof(ThemedWindowIconHelper),
        new PropertyMetadata(false, OnUseAppIconChanged));

    public static bool GetUseAppIcon(Image image) => (bool)image.GetValue(UseAppIconProperty);
    public static void SetUseAppIcon(Image image, bool value) => image.SetValue(UseAppIconProperty, value);

    public static readonly DependencyProperty MinimumFrameSizeProperty = DependencyProperty.RegisterAttached(
        "MinimumFrameSize", typeof(double), typeof(ThemedWindowIconHelper),
        new PropertyMetadata(16.0, (sender, _) =>
        {
            if (sender is Image image && GetUseAppIcon(image)) UpdateImage(image, new RoutedEventArgs());
        }));

    public static double GetMinimumFrameSize(Image image) => (double)image.GetValue(MinimumFrameSizeProperty);
    public static void SetMinimumFrameSize(Image image, double value) => image.SetValue(MinimumFrameSizeProperty, value);

    internal static BitmapFrame GetFrame(double pixelSize) =>
        Frames.Value.FirstOrDefault(frame => frame.PixelWidth >= pixelSize) ?? Frames.Value[^1];

    private static void OnUseAppIconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Image image) return;
        if ((bool)e.NewValue)
        {
            image.Loaded += UpdateImage;
            image.SizeChanged += UpdateImage;
            image.DpiChanged += UpdateImage;
            image.UseLayoutRounding = true;
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            UpdateImage(image, new RoutedEventArgs());
        }
        else
        {
            image.Loaded -= UpdateImage;
            image.SizeChanged -= UpdateImage;
            image.DpiChanged -= UpdateImage;
        }
    }

    private static void UpdateImage(object sender, RoutedEventArgs e)
    {
        var image = (Image)sender;
        var width = image.ActualWidth > 0 ? image.ActualWidth : image.Width;
        var height = image.ActualHeight > 0 ? image.ActualHeight : image.Height;
        var size = Math.Min(width, height);
        if (!double.IsFinite(size) || size <= 0) size = 16;
        image.Source = GetFrame(Math.Max(GetMinimumFrameSize(image),
            Math.Ceiling(size * VisualTreeHelper.GetDpi(image).PixelsPerDip)));
    }
}
