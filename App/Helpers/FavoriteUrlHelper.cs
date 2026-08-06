using System.Windows.Media;

using Lertaro.App.Services.ShellIcons;
namespace Lertaro.App.Helpers;

// Helpers for favorites that point at a web address (http/https) rather than a local/shell path.
// Opening such favorites already works via Process.Start(UseShellExecute=true) -> default browser; these
// just give them a display name (the full URL) and a default globe icon since the shell has no icon for a URL.
public static class FavoriteUrlHelper
{
    // Globe icon (same artwork as the WebUrl instant provider).
    private const string GlobeIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z";

    // True when the path is an http/https web address.
    public static bool IsWebUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Uri.TryCreate(path.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    // Globe icon for web-address favorites. Rebuilt (not cached) so it reflects the current
    // theme's AccentBlue instead of freezing the color from whichever theme was active first.
    public static ImageSource Icon => ShellIconHelper.CreateVectorIcon(GlobeIcon, "AccentBlue");
}
