using System.Runtime.InteropServices;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.Plugins.BrowserData;

// Decodes cached browser favicon bytes once during profile loading and creates a fresh HBITMAP for each
// result row. The host owns and deletes the returned handle after it materializes the instant result.
internal static class BrowserFaviconIconLoader
{
    public static BitmapSource? Decode(byte[] imageData)
    {
        try
        {
            using var stream = new MemoryStream(imageData, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = 64;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static IntPtr ToHBitmap(BitmapSource? source)
    {
        if (source == null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
            return IntPtr.Zero;

        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[source.PixelHeight * stride];
        formatted.CopyPixels(pixels, stride, 0);
        var handle = CreateDibSection(source.PixelWidth, source.PixelHeight, out var bits);
        if (handle == IntPtr.Zero || bits == IntPtr.Zero)
            return IntPtr.Zero;

        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return handle;
    }

    private static IntPtr CreateDibSection(int width, int height, out IntPtr bits)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            }
        };
        return CreateDibSectionNative(IntPtr.Zero, ref info, 0, out bits, IntPtr.Zero, 0);
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    private static extern IntPtr CreateDibSectionNative(
        IntPtr hdc,
        [In] ref BitmapInfo info,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint ImageSize;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }
}
