using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Flow.Launcher.Plugin.SharedCommands;

public static class SearchWeb
{
    // Keep the four-argument signatures for plugins compiled against earlier bridge assemblies.
    public static void OpenInBrowserWindow(this string url, string browserPath, bool inPrivate, string privateArg) =>
        OpenInBrowserWindow(url, browserPath, inPrivate, privateArg, string.Empty);
    public static void OpenInBrowserTab(this string url, string browserPath, bool inPrivate, string privateArg) =>
        OpenInBrowserTab(url, browserPath, inPrivate, privateArg, string.Empty);
    public static void OpenInBrowserWindow(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "", string extraArgs = "") =>
        Open(url, browserPath, inPrivate, privateArg, extraArgs, newWindow: true);
    public static void OpenInBrowserTab(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "", string extraArgs = "") =>
        Open(url, browserPath, inPrivate, privateArg, extraArgs, newWindow: false);
    public static void OpenInBrowser(this string url, string browserPath = "", bool inPrivate = false, string privateArg = "") =>
        OpenInBrowserTab(url, browserPath, inPrivate, privateArg);

    private static void Open(string url, string browserPath, bool inPrivate, string privateArg, string extraArgs, bool newWindow)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (string.IsNullOrWhiteSpace(browserPath)) browserPath = GetDefaultBrowserPath();
        try
        {
            Process.Start(BuildStartInfo(url, browserPath, inPrivate, privateArg, extraArgs, newWindow))?.Dispose();
        }
        catch (Win32Exception) when (!string.IsNullOrWhiteSpace(browserPath))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
    }

    internal static ProcessStartInfo BuildStartInfo(string url, string browserPath, bool inPrivate, string privateArg, string extraArgs, bool newWindow)
    {
        if (string.IsNullOrWhiteSpace(browserPath)) return new ProcessStartInfo(url) { UseShellExecute = true };
        var windowArg = newWindow && !Path.GetFileName(browserPath).Equals("iexplore.exe", StringComparison.OrdinalIgnoreCase) ? "--new-window " : "";
        return new ProcessStartInfo(browserPath)
        {
            UseShellExecute = true,
            Arguments = windowArg + (inPrivate && !string.IsNullOrWhiteSpace(privateArg) ? privateArg + " " : "")
                + (string.IsNullOrWhiteSpace(extraArgs) ? "" : extraArgs + " ") + url
        };
    }

    private static string GetDefaultBrowserPath()
    {
        try
        {
            const string root = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\";
            using var latest = Registry.CurrentUser.OpenSubKey(root + @"UserChoiceLatest\ProgId");
            using var legacy = Registry.CurrentUser.OpenSubKey(root + "UserChoice");
            var progId = latest?.GetValue("ProgId") as string ?? legacy?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId)) return string.Empty;
            using var command = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command");
            var value = (command?.GetValue(null) as string)?.Trim();
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var end = value.StartsWith('"') ? value.IndexOf('"', 1) : value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
            return value.StartsWith('"') ? end > 1 ? value[1..end] : string.Empty
                : end >= 4 ? value[..end] : string.Empty;
        }
        catch { return string.Empty; }
    }
}
