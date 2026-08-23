using System.Diagnostics;

namespace Flow.Launcher.Plugin.SharedCommands;

public static class SearchWeb
{
    public static void OpenInBrowserWindow(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "")
    {
        OpenInBrowser(url, browserPath, inPrivate, privateArg);
    }

    public static void OpenInBrowserTab(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "")
    {
        OpenInBrowser(url, browserPath, inPrivate, privateArg);
    }

    public static void OpenInBrowser(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "")
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else
            {
                var args = string.IsNullOrWhiteSpace(privateArg) ? url : $"{privateArg} {url}";
                Process.Start(new ProcessStartInfo(browserPath, args) { UseShellExecute = true });
            }
        }
        catch { }
    }
}
