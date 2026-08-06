using System.Diagnostics;
using Lertaro.Core;

namespace Lertaro.App.Helpers;

/// <summary>
/// Opens external URLs (hyperlink clicks in settings pages) in the user's default browser.
/// Failures (e.g. no registered URL handler) are logged and swallowed rather than surfaced --
/// these are triggered by non-critical informational links, not user-initiated file operations.
/// </summary>
public static class UrlLauncher
{
    public static void Open(Uri uri) => Open(uri.AbsoluteUri);

    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[UrlLauncher] Failed to open URL: {ex.Message}", LogLevel.Warn);
        }
    }
}
