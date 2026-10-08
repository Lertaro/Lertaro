using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

// Resize the fixed blue brand artwork to the taskbar's current icon size.
internal static class TrayIconRenderer
{
    public static Icon? CreateIcon(out IntPtr hIcon)
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

            g.DrawImage(originalBitmap,
                new Rectangle(0, 0, iconWidth, iconHeight),
                0, 0, originalBitmap.Width, originalBitmap.Height,
                GraphicsUnit.Pixel);
        }

        hIcon = coloredBitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}
