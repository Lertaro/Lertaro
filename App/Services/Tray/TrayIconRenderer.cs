using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Services.Tray;

internal static class TrayIconRenderer
{
    public static Icon CreateIcon() => CreateIcon(SystemInformation.SmallIconSize.Width);

    /// <summary>
    /// Paints the brand mark at exactly the shell's small-icon size and wraps it in a one-entry ICO.
    /// The tray only accepts an HICON, so this is as close to "straight from the vector" as the
    /// platform allows — the point is that no pre-baked small frame is in the loop anymore.
    /// </summary>
    internal static Icon CreateIcon(int pixelSize)
    {
        using var stream = new MemoryStream(ToIco(Paint(pixelSize), pixelSize));
        return new Icon(stream, pixelSize, pixelSize);
    }

    static byte[] Paint(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(BrandLogo.Create(), new Rect(0, 0, size, size));
        }
        var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        // A DIB stores straight, not premultiplied, alpha.
        var straight = new FormatConvertedBitmap(rendered, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        straight.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    static byte[] ToIco(byte[] bgra, int size)
    {
        var stride = size * 4;
        var maskStride = (size + 31) / 32 * 4;
        var imageSize = 40 + stride * size + maskStride * size;
        using var buffer = new MemoryStream();
        using var w = new BinaryWriter(buffer);

        w.Write((ushort)0);                      // reserved
        w.Write((ushort)1);                      // type: icon
        w.Write((ushort)1);                      // one entry
        w.Write((byte)(size >= 256 ? 0 : size)); // width, 0 means 256
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);                        // palette size
        w.Write((byte)0);                        // reserved
        w.Write((ushort)1);                      // planes
        w.Write((ushort)32);                     // bit count
        w.Write((uint)imageSize);
        w.Write((uint)22);                       // image offset, straight past the directory

        w.Write((uint)40);                       // BITMAPINFOHEADER size
        w.Write(size);                           // width
        w.Write(size * 2);                       // height: XOR image plus the AND mask
        w.Write((ushort)1);                      // planes
        w.Write((ushort)32);                     // bit count
        w.Write((uint)0);                        // BI_RGB, i.e. an uncompressed 32bpp DIB
        w.Write((uint)(stride * size + maskStride * size));
        w.Write(0); w.Write(0);                  // resolution
        w.Write(0); w.Write(0);                  // colors used / important

        for (var y = size - 1; y >= 0; y--) w.Write(bgra, y * stride, stride);
        w.Write(new byte[maskStride * size]);    // all-zero mask: the alpha channel carries the shape

        w.Flush();
        return buffer.ToArray();
    }
}
