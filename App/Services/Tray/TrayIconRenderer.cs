using System.Drawing.Imaging;
using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

// The GDI+ recolor pipeline behind the tray icon's theme-following color -- split out of
// TrayIconService.cs to keep that file under the project's line limit.
internal static class TrayIconRenderer
{
    public static Icon? CreateThemedIcon(Color drawingColor, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;

        var resourceUri = new Uri("pack://application:,,,/Lertaro.App;component/tray.png", UriKind.Absolute);
        var resourceInfo = Application.GetResourceStream(resourceUri);
        if (resourceInfo == null) return null;

        using var originalStream = resourceInfo.Stream;
        using var originalBitmap = new Bitmap(originalStream);

        // Target dimensions based on current DPI scaling.
        var iconWidth = SystemInformation.SmallIconSize.Width;
        var iconHeight = SystemInformation.SmallIconSize.Height;

        using var coloredBitmap = new Bitmap(iconWidth, iconHeight);
        using (var g = Graphics.FromImage(coloredBitmap))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            using var attributes = new ImageAttributes();
            var colorMatrix = new ColorMatrix(new float[][]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, drawingColor.A / 255f, 0 },
                new float[] { drawingColor.R / 255f, drawingColor.G / 255f, drawingColor.B / 255f, 0, 1 }
            });
            attributes.SetColorMatrix(colorMatrix);
            g.DrawImage(originalBitmap,
                new Rectangle(0, 0, iconWidth, iconHeight),
                0, 0, originalBitmap.Width, originalBitmap.Height,
                GraphicsUnit.Pixel, attributes);
        }

        hIcon = coloredBitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
