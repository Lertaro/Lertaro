using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

[TestClass]
public class ShellIconImageHashTests
{
    [StaTestMethod]
    public void GetHashFromImage_NullOrUnfrozenImage_ReturnsNull()
    {
        var hashNull = ShellIconImageHash.GetHashFromImage(null!);
        Assert.IsNull(hashNull);

        var unfrozenBmp = BitmapSource.Create(
            1, 1, 96, 96,
            PixelFormats.Pbgra32, null,
            new byte[] { 255, 0, 0, 255 }, 4);

        var hashUnfrozen = ShellIconImageHash.GetHashFromImage(unfrozenBmp);
        Assert.IsNull(hashUnfrozen);
    }

    [StaTestMethod]
    public void GetHashFromImage_IdenticalBitmaps_ReturnsSameHash()
    {
        var bmp1 = BitmapSource.Create(
            2, 2, 96, 96,
            PixelFormats.Pbgra32, null,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, 8);
        bmp1.Freeze();

        var bmp2 = BitmapSource.Create(
            2, 2, 96, 96,
            PixelFormats.Pbgra32, null,
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255 }, 8);
        bmp2.Freeze();

        var hash1 = ShellIconImageHash.GetHashFromImage(bmp1);
        var hash2 = ShellIconImageHash.GetHashFromImage(bmp2);

        Assert.IsNotNull(hash1);
        Assert.AreEqual(hash1, hash2);
    }

    [StaTestMethod]
    public void GetHashFromImage_DifferentBitmaps_ReturnsDifferentHash()
    {
        var bmp1 = BitmapSource.Create(
            1, 1, 96, 96,
            PixelFormats.Pbgra32, null,
            new byte[] { 255, 0, 0, 255 }, 4);
        bmp1.Freeze();

        var bmp2 = BitmapSource.Create(
            1, 1, 96, 96,
            PixelFormats.Pbgra32, null,
            new byte[] { 0, 255, 0, 255 }, 4);
        bmp2.Freeze();

        var hash1 = ShellIconImageHash.GetHashFromImage(bmp1);
        var hash2 = ShellIconImageHash.GetHashFromImage(bmp2);

        Assert.IsNotNull(hash1);
        Assert.IsNotNull(hash2);
        Assert.AreNotEqual(hash1, hash2);
    }
}
