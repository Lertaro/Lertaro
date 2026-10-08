using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Tests.Services.Theme;

[TestClass]
public sealed class BrandIconTests
{
    [STATestMethod]
    public void Apply_BrandArtworkIsBlueAndSharedByWindowAndTitleBar()
    {
        _ = Application.ResourceAssembly;
        var window = new Window();
        var image = new System.Windows.Controls.Image();
        try
        {
            window.Resources["AccentBlue"] = Brushes.Red;
            ThemedWindowIconHelper.Apply(window);
            ThemedWindowIconHelper.Apply(image, window);
            Assert.AreSame(window.Icon, image.Source);
            Assert.IsTrue(window.Icon.IsFrozen);

            var source = Assert.IsInstanceOfType<BitmapSource>(window.Icon);
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);
            var visible = 0;
            var wrongColor = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] == 0) continue;
                visible++;
                if (pixels[i] != 0xD8 || pixels[i + 1] != 0x98 || pixels[i + 2] != 0)
                    wrongColor++;
            }
            Assert.IsGreaterThan(0, visible);
            Assert.AreEqual(0, wrongColor, "Every visible artwork pixel should use the fixed #0098D8 brand blue.");
        }
        finally
        {
            window.Close();
        }
    }
}
