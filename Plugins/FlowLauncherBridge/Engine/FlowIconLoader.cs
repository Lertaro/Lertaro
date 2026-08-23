using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Handles image and SVG icon loading using purely standard library WPF primitives.
/// No external dependencies required.
/// </summary>
public static class FlowIconLoader
{
    public static IntPtr LoadIconAsHBitmap(string? iconPath, int targetSize = 64)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            return IntPtr.Zero;

        try
        {
            var ext = Path.GetExtension(iconPath).ToLowerInvariant();
            BitmapSource? bitmapSource = null;

            if (ext == ".svg")
            {
                bitmapSource = LoadSvgUsingStdLib(iconPath, targetSize);
            }
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".ico" or ".bmp" or ".gif")
            {
                bitmapSource = LoadRasterImage(iconPath);
            }

            if (bitmapSource != null)
            {
                return ConvertBitmapSourceToHBitmap(bitmapSource);
            }
        }
        catch
        {
            // Fall back cleanly if icon decoding fails
        }

        return IntPtr.Zero;
    }

    private static BitmapSource? LoadSvgUsingStdLib(string path, int targetSize)
    {
        try
        {
            var xml = File.ReadAllText(path);
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
                return null;

            var drawingGroup = new DrawingGroup();
            using (var dc = drawingGroup.Open())
            {
                RenderSvgElement(root, dc);
            }

            if (drawingGroup.Bounds.Width <= 0 || drawingGroup.Bounds.Height <= 0)
                return null;

            var scale = (double)targetSize / Math.Max(drawingGroup.Bounds.Width, drawingGroup.Bounds.Height);
            var width = Math.Max(1, (int)Math.Ceiling(drawingGroup.Bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(drawingGroup.Bounds.Height * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawDrawing(drawingGroup);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }

    private static void RenderSvgElement(XElement element, DrawingContext dc)
    {
        foreach (var el in element.DescendantsAndSelf())
        {
            var name = el.Name.LocalName.ToLowerInvariant();
            var fillBrush = ParseBrush(el.Attribute("fill")?.Value);
            var strokeBrush = ParseBrush(el.Attribute("stroke")?.Value);
            var strokeWidth = ParseDouble(el.Attribute("stroke-width")?.Value, 1.0);
            Pen? pen = null;
            if (strokeBrush != null)
            {
                pen = new Pen(strokeBrush, strokeWidth);
                pen.Freeze();
            }

            if (name == "path")
            {
                var d = el.Attribute("d")?.Value;
                if (!string.IsNullOrWhiteSpace(d))
                {
                    try
                    {
                        var geometry = Geometry.Parse(d);
                        geometry.Freeze();
                        dc.DrawGeometry(fillBrush, pen, geometry);
                    }
                    catch { }
                }
            }
            else if (name == "rect")
            {
                var x = ParseDouble(el.Attribute("x")?.Value, 0);
                var y = ParseDouble(el.Attribute("y")?.Value, 0);
                var w = ParseDouble(el.Attribute("width")?.Value, 0);
                var h = ParseDouble(el.Attribute("height")?.Value, 0);
                var rx = ParseDouble(el.Attribute("rx")?.Value, 0);
                var ry = ParseDouble(el.Attribute("ry")?.Value, rx);

                if (w > 0 && h > 0)
                {
                    if (rx > 0 || ry > 0)
                        dc.DrawRoundedRectangle(fillBrush, pen, new Rect(x, y, w, h), rx, ry);
                    else
                        dc.DrawRectangle(fillBrush, pen, new Rect(x, y, w, h));
                }
            }
            else if (name == "circle")
            {
                var cx = ParseDouble(el.Attribute("cx")?.Value, 0);
                var cy = ParseDouble(el.Attribute("cy")?.Value, 0);
                var r = ParseDouble(el.Attribute("r")?.Value, 0);
                if (r > 0)
                    dc.DrawEllipse(fillBrush, pen, new Point(cx, cy), r, r);
            }
            else if (name == "text")
            {
                var text = el.Value;
                var x = ParseDouble(el.Attribute("x")?.Value, 0);
                var y = ParseDouble(el.Attribute("y")?.Value, 0);
                var fontSize = ParseDouble(el.Attribute("font-size")?.Value, 14);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var ft = new FormattedText(
                        text,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei, Segoe UI"),
                        fontSize,
                        fillBrush ?? Brushes.Black,
                        96.0 / 96.0);
                    dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
                }
            }
        }
    }

    private static Brush? ParseBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(value)!;
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static double ParseDouble(string? value, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : defaultValue;
    }

    private static BitmapSource? LoadRasterImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static IntPtr ConvertBitmapSourceToHBitmap(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0)
            return IntPtr.Zero;

        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        var handle = CreateDIBSection(IntPtr.Zero, width, height, out var ppvBits);
        if (handle != IntPtr.Zero && ppvBits != IntPtr.Zero)
        {
            // Copy pixels in top-down format
            Marshal.Copy(pixels, 0, ppvBits, pixels.Length);
            return handle;
        }

        return IntPtr.Zero;
    }

    private static IntPtr CreateDIBSection(IntPtr hdc, int width, int height, out IntPtr ppvBits)
    {
        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        return CreateDIBSectionNative(hdc, ref bi, 0, out ppvBits, IntPtr.Zero, 0);
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDIBSection", SetLastError = true)]
    private static extern IntPtr CreateDIBSectionNative(IntPtr hdc, [In] ref BITMAPINFO pbmi, uint pila, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }
}
