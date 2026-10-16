using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.Theme;
using Lertaro.App.Services.Tray;

namespace Lertaro.App.Tests.Services.Theme;

// The brand mark is a frozen WPF drawing, so one STA thread for the whole class keeps the
// pack-resource stream list and the visual tree on a single owner.
[STATestClass]
// WPF's pack-resource stream list is shared and is not safe to open from parallel STA tests.
[DoNotParallelize]
public sealed class BrandIconTests
{
    public BrandIconTests() => _ = Application.ResourceAssembly;

    static readonly Color SampleAccent = Color.FromRgb(0x05, 0x77, 0xbc);

    [STATestMethod]
    public void Apply_WindowKeepsIcoFrameBecauseNativeIconsTakeNoVector()
    {
        var window = new Window();
        try
        {
            ThemedWindowIconHelper.Apply(window);
            Assert.IsTrue(window.Icon.IsFrozen);
            var frame = Assert.IsInstanceOfType<BitmapFrame>(window.Icon);
            Assert.IsInstanceOfType<IconBitmapDecoder>(frame.Decoder);
            Assert.AreEqual(32, frame.PixelWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void Apply_ImageGetsAFrozenVectorMark_NotAnIcoFrame()
    {
        var image = new System.Windows.Controls.Image();
        ThemedWindowIconHelper.Apply(image);
        var mark = Assert.IsInstanceOfType<DrawingImage>(image.Source);
        Assert.IsTrue(mark.IsFrozen, "A shared mark must not be thread-affine.");
    }

    [STATestMethod]
    public void UseAppIcon_AttachedPropertyAssignsTheMark()
    {
        var image = new System.Windows.Controls.Image();
        ThemedWindowIconHelper.SetUseAppIcon(image, true);
        Assert.IsInstanceOfType<DrawingImage>(image.Source);
    }

    [STATestMethod]
    public void RepaintBrandMarks_AfterAThemeChange_HandsOutANewMark()
    {
        var image = new System.Windows.Controls.Image();
        ThemedWindowIconHelper.Apply(image);
        var before = image.Source;
        ThemedWindowIconHelper.RepaintBrandMarks();
        Assert.AreNotSame(before, image.Source, "A repaint must replace the frozen mark, not reuse it.");
        Assert.IsInstanceOfType<DrawingImage>(image.Source);
    }

    [STATestMethod]
    [DataRow("#0060A0", "Light")]
    [DataRow("#63B3ED", "Dark")]
    [DataRow("#3E5E78", "Nord")]
    [DataRow("#AF3154", "Sakura")]
    [DataRow("#FF198C", "Cyberpunk")]
    [DataRow("#005B87", "WeatheringBlue")]
    [DataRow("#B688FF", "NeonGenesis")]
    [DataRow("#9B2048", "SakuraBloom")]
    [DataRow("#000000", "Black")]
    [DataRow("#FFFFFF", "White")]
    public void Create_AccentPaintsEveryLayerFlat(string accentHex, string themeName)
    {
        var accent = (Color)ColorConverter.ConvertFromString(accentHex);
        var paints = ((DrawingGroup)((DrawingImage)BrandLogo.Create(accent)).Drawing).Children
            .Cast<GeometryDrawing>().Select(drawing => ((SolidColorBrush)drawing.Brush).Color).ToList();

        Assert.AreEqual(5, paints.Count, $"{themeName}: the mark keeps its five source layers.");
        foreach (var paint in paints)
            Assert.AreEqual(accent, paint, $"{themeName}: every layer is the accent itself.");
    }

    [STATestMethod]
    public void BrandLogo_LensHoleStaysPunchedThrough()
    {
        // The source mark relies on the even-odd fill rule to turn the lens outline into a ring.
        // The lens sits in the top-left of the 1024 box, so its center is at 31.7% of the mark.
        var bitmap = Render(64);
        Assert.AreEqual(0, bitmap.GetPixel(20, 20).A, "The middle of the lens must be transparent.");
        Assert.IsGreaterThan(200, bitmap.GetPixel(20, 4).A, "The lens rim must be painted.");
    }

    [STATestMethod]
    public void BrandLogo_KeepsItsSilhouetteAtTraySize()
    {
        var bitmap = Render(16);
        Assert.AreEqual(0, bitmap.GetPixel(5, 5).A, "The lens hole survives at 16 pixels.");
        var painted = 0;
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                if (bitmap.GetPixel(x, y).A > 128) painted++;
        Assert.IsGreaterThan(60, painted, "The mark must still cover a usable share of a 16-pixel cell.");
        Assert.IsLessThan(220, painted, "The mark must not fill the cell edge to edge.");
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
    [DataRow(41, 48)]
    [DataRow(49, 64)]
    public void GetFrame_PhysicalPixelSize_PreservesMatchingOrNextLargerArtwork(int requested, int expected)
    {
        var frame = ThemedWindowIconHelper.GetFrame(requested);
        Assert.AreEqual(expected, frame.PixelWidth);
        Assert.AreEqual(expected, frame.PixelHeight);
        CollectionAssert.AreEqual(Pixels(ReadFrame(expected)), Pixels(frame));
    }

    [STATestMethod]
    [DataRow(16)]
    [DataRow(20)]
    [DataRow(24)]
    [DataRow(32)]
    [DataRow(40)]
    [DataRow(48)]
    public void CreateIcon_RendersTheVectorAtTheRequestedSize(int size)
    {
        using var icon = TrayIconRenderer.CreateIcon(size);
        Assert.AreEqual(size, icon.Width);
        Assert.AreEqual(size, icon.Height);
        using var bitmap = icon.ToBitmap();
        Assert.AreEqual(size, bitmap.Width);
        // The lens sits at 31.7% of the mark and its hole must survive the rasterization.
        var center = (int)(size * 0.317);
        Assert.AreEqual(0, bitmap.GetPixel(center, center).A, $"Lens hole at {size}px.");
        Assert.IsGreaterThan(200, bitmap.GetPixel(center, (int)(size * 0.098)).A, $"Lens rim at {size}px.");
    }

    [STATestMethod]
    public void CreateIcon_IsPaintedFlatWithTheAccent_NotTheIcoArtwork()
    {
        // No Application exists under the test host, so the mark falls back to the source accent.
        using var icon = TrayIconRenderer.CreateIcon(48);
        using var bitmap = icon.ToBitmap();
        var painted = bitmap.GetPixel(15, 4);
        Assert.AreEqual(0x05, painted.R);
        Assert.AreEqual(0x77, painted.G);
        Assert.AreEqual(0xbc, painted.B);
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
            Width = 600,
            Height = height,
            IsDynamicScalingEnabled = true,
            IsIconOnLeft = onLeft,
            LeftIconSize = 34,
            RightIconSize = 34
        };
        VerifySearchIconLayout(control, onLeft);
    }

    [STATestMethod]
    public void FullSearch_IconIs24PixelsWithoutIncreasingHeaderHeight()
    {
        var control = new SearchBoxControl
        {
            Width = 600,
            Height = 26,
            FontSize = 15,
            Padding = new Thickness(0),
            IsIconOnLeft = true,
            LeftIconSize = 26,
            RightIconSize = 26
        };
        var image = VerifySearchIconLayout(control, true);
        Assert.AreEqual(24.0, image.ActualHeight, 0.5);
    }

    private static System.Windows.Controls.Image VerifySearchIconLayout(SearchBoxControl control, bool onLeft)
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
        Assert.IsInstanceOfType<DrawingImage>(image.Source);
        return image;
    }

    private static System.Drawing.Bitmap Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(BrandLogo.Create(SampleAccent), new Rect(0, 0, size, size));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        var result = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var offset = (y * size + x) * 4;
                result.SetPixel(x, y, System.Drawing.Color.FromArgb(
                    pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]));
            }
        return result;
    }

    private static BitmapFrame ReadFrame(int size)
    {
        var uri = new Uri("pack://application:,,,/Lertaro.App;component/logo.ico");
        using var stream = Application.GetResourceStream(uri)!.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames.Single(frame => frame.PixelWidth == size);
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
