using Application = System.Windows.Application;

namespace Lertaro.App.Services.Tray;

internal static class TrayIconRenderer
{
    public static Icon CreateIcon() => CreateIcon(SystemInformation.SmallIconSize.Width);

    internal static Icon CreateIcon(int pixelSize)
    {
        var uri = new Uri("pack://application:,,,/Lertaro.App;component/logo.ico", UriKind.Absolute);
        using var stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        return new Icon(stream, pixelSize, pixelSize);
    }
}
