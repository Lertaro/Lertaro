using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Tessoa;

internal static class TessoaNavigation
{
    internal static string? Resolve(string? configured, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Supported(configured) ? configured : null;
        if (Supported(observed)) return observed;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\tessoa.exe");
                if (key?.GetValue(null) is string path && Supported(path)) return path;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tessoa", "tessoa.exe");
        return Supported(installed) ? installed : null;
    }

    private static bool Supported(string? path)
    {
        if (!TessoaQuery.IsPath(path) || !string.Equals(Path.GetFileName(path), "tessoa.exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path)) return false;
        try { return FileVersionInfo.GetVersionInfo(path!).Comments == "tessoa-file-app-api=1"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { return false; }
    }

    internal static bool Navigate(TessoaWindow? window, string path, bool reveal)
    {
        if (!TessoaQuery.IsPath(path)) return false;
        var configured = PluginSettingsService.GetSetting(TessoaPlugin.Id, "ExecutablePath", "");
        var observed = window?.Image ?? (string.IsNullOrWhiteSpace(configured) ? TessoaWindow.Visible().FirstOrDefault()?.Image : null);
        var exe = Resolve(configured, observed);
        path = Path.TrimEndingDirectorySeparator(path);
        if (exe == null || !(Directory.Exists(path) || (reveal && File.Exists(path)))) return false;
        var tabs = window == null ? null : TessoaQuery.Read(window, "query-tabs");
        try
        {
            return Send(exe, path, reveal, window?.Handle ?? 0, tabs, info =>
            {
                using var process = Process.Start(info);
                if (process == null) return -1;
                return process.WaitForExit(3500) ? process.ExitCode : 0;
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException) { return false; }
    }

    internal static bool Send(string exe, string path, bool reveal, nint window, string? tabs, Func<ProcessStartInfo, int> run)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        info.ArgumentList.Add((reveal ? "--reveal-window=" : "--open-window=") + path);
        if (window == 0 || tabs == "") return run(info) == 0;
        if (!TessoaQuery.SupportsTabs(tabs)) return false;
        info.ArgumentList.Add("--in-tab=" + window.ToInt64().ToString(CultureInfo.InvariantCulture));
        var code = run(info);
        if (code != 1) return code == 0;
        info.ArgumentList.RemoveAt(1);
        return run(info) == 0;
    }
}
