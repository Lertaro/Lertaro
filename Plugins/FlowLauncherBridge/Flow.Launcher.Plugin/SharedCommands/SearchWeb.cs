using System.Diagnostics;

namespace Flow.Launcher.Plugin.SharedCommands;

/// <summary>
/// Web search helpers for opening search URLs.
/// </summary>
public static class SearchWeb
{
    public static void OpenUrlInBrowser(string url, string? browserPath = null, bool inPrivate = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (string.IsNullOrEmpty(browserPath))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                var args = inPrivate ? $"--incognito \"{url}\"" : $"\"{url}\"";
                Process.Start(new ProcessStartInfo(browserPath, args) { UseShellExecute = false });
            }
        }
        catch
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
