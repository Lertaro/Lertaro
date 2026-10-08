using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.Theme;
using Lertaro.App.Services.Tray;

namespace Lertaro.App.Tests.Services.Theme;

[TestClass]
// WPF's pack-resource stream list is shared and is not safe to open from parallel STA tests.
[DoNotParallelize]
public sealed class BrandIconTests
{
    public BrandIconTests() => _ = Application.ResourceAssembly;

    [STATestMethod]
    public void Apply_WindowAndTitleBarUseOriginalIcoFramesRegardlessOfTheme()
    {
        var window = new Window();
        var image = new System.Windows.Controls.Image { Width = 32, Height = 32 };
        try
        {
            window.Resources["AccentBlue"] = Brushes.Red;
            ThemedWindowIconHelper.Apply(window);
            ThemedWindowIconHelper.Apply(image, window);
            Assert.AreSame(window.Icon, image.Source);
            Assert.IsTrue(window.Icon.IsFrozen);
            var source = Assert.IsInstanceOfType<BitmapFrame>(window.Icon);
            Assert.IsInstanceOfType<IconBitmapDecoder>(source.Decoder);
            CollectionAssert.AreEqual(Pixels(ReadFrame(32)), Pixels(source), "Theme colors must not recolor the ICO artwork.");
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    [DataRow(16, 16)]
    [DataRow(20, 20)]
    [DataRow(24, 24)]
    [DataRow(32, 32)]
    [DataRow(40, 40)]
    [DataRow(48, 48)]
    [DataRow(64, 64)]
    [DataRow(128, 128)]
    [DataRow(256, 256)]
    [DataRow(17, 20)]
    [DataRow(25, 32)]
    [DataRow(33, 40)]
    [DataRow(49, 64)]
    public void GetFrame_PhysicalPixelSize_PreservesMatchingOrNextLargerArtwork(int requested, int expected)
    {
        var frame = ThemedWindowIconHelper.GetFrame(requested);
        Assert.AreEqual(expected, frame.PixelWidth);
        Assert.AreEqual(expected, frame.PixelHeight);
        CollectionAssert.AreEqual(Pixels(ReadFrame(expected)), Pixels(frame));
    }

    [STATestMethod]
    public void UseAppIcon_ImageIsResized_ChangesToMatchingFrame()
    {
        var image = new System.Windows.Controls.Image();
        ThemedWindowIconHelper.SetUseAppIcon(image, true);
        image.Measure(new Size(16, 16));
        image.Arrange(new Rect(0, 0, 16, 16));
        image.UpdateLayout();
        Assert.AreEqual(16, Assert.IsInstanceOfType<BitmapFrame>(image.Source).PixelWidth);

        image.Measure(new Size(24, 24));
        image.Arrange(new Rect(0, 0, 24, 24));
        image.UpdateLayout();
        Assert.AreEqual(24, Assert.IsInstanceOfType<BitmapFrame>(image.Source).PixelWidth);
    }

    [STATestMethod]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(24)]
    [DataRow(32)]
    [DataRow(40)]
    [DataRow(48)]
    public void CreateIcon_TraySize_UsesMatchingIcoFrame(int size)
    {
        using var icon = TrayIconRenderer.CreateIcon(size);
        Assert.AreEqual(size, icon.Width);
        Assert.AreEqual(size, icon.Height);
        using var bitmap = icon.ToBitmap();
        var expected = Pixels(ReadFrame(size));
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var color = bitmap.GetPixel(x, y);
            var offset = (y * size + x) * 4;
            Assert.AreEqual(expected[offset + 3], color.A, $"Alpha at ({x}, {y}).");
            if (color.A == 0) continue;
            Assert.AreEqual(expected[offset], color.B, $"Blue at ({x}, {y}).");
            Assert.AreEqual(expected[offset + 1], color.G, $"Green at ({x}, {y}).");
            Assert.AreEqual(expected[offset + 2], color.R, $"Red at ({x}, {y}).");
        }
    }

    private static BitmapFrame ReadFrame(int size)
    {
        var uri = new Uri("pack://application:,,,/Lertaro.App;component/logo.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.Single(frame => frame.PixelWidth == size);
    }

    [STATestMethod]
    [DataRow(40, true)]
    [DataRow(40, false)]
    [DataRow(50, true)]
    [DataRow(50, false)]
    [DataRow(70, false)]
    [DataRow(100, false)]
    public void QuickSearch_IconFitsExistingRowAndStaysCentered(int height, bool onLeft)
    {
        var control = new SearchBoxControl
        {
            Width = 600, Height = height, IsDynamicScalingEnabled = true, IsIconOnLeft = onLeft,
            LeftIconSize = 34, RightIconSize = 34, MinimumIconFrameSize = 32
        };
        VerifySearchIconLayout(control, onLeft, 32);
    }

    [STATestMethod]
    public void FullSearch_IconIs24PixelsWithoutIncreasingHeaderHeight()
    {
        var control = new SearchBoxControl
        {
            Width = 600, Height = 26, FontSize = 15, Padding = new Thickness(0), IsIconOnLeft = true,
            LeftIconSize = 26, RightIconSize = 26, MinimumIconFrameSize = 24
        };
        var image = VerifySearchIconLayout(control, true, 24);
        Assert.AreEqual(24.0, image.ActualHeight, 0.5);
    }

    private static System.Windows.Controls.Image VerifySearchIconLayout(SearchBoxControl control, bool onLeft, int minimumFrame)
    {
        control.Measure(new Size(control.Width, control.Height));
        control.Arrange(new Rect(0, 0, control.Width, control.Height));
        control.UpdateLayout();
        var image = Assert.IsInstanceOfType<System.Windows.Controls.Image>(control.FindName(onLeft ? "LeftBrandIcon" : "RightBrandIcon"));
        Assert.AreEqual(control.Height, control.DesiredSize.Height, 0.5, "The icon must not grow the search row.");
        Assert.IsGreaterThan(0.0, image.ActualHeight);
        Assert.IsLessThanOrEqualTo(control.Height - control.Padding.Top - control.Padding.Bottom, image.ActualHeight);
        var top = image.TransformToAncestor(control).Transform(new Point()).Y;
        Assert.AreEqual(control.Height / 2, top + image.ActualHeight / 2, 0.5, "The icon must be vertically centered.");
        Assert.IsGreaterThanOrEqualTo(minimumFrame, Assert.IsInstanceOfType<BitmapFrame>(image.Source).PixelWidth);
        return image;
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
