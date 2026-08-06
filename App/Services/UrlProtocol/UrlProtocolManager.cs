using Microsoft.Win32;
using Lertaro.Core;

namespace Lertaro.App.Services.UrlProtocol;

// Registers "lertaro://" as a URL protocol under HKCU\Software\Classes, so the OS can launch (or,
// via the existing single-instance mutex/pipe relay -- see App.xaml.cs -- activate) Lertaro from a
// lertaro:// link. HKCU rather than HKCR: no elevation needed, matches StartupManager's own Run-key
// convention, and merges into the effective HKCR view for the current user regardless. Runs once per
// app startup (see App.xaml.cs) rather than only from the installer, so a portable copy moved to a new
// path -- or an app relocated after an update -- self-heals the registered command on its next launch.
public static class UrlProtocolManager
{
    private const string ProtocolKeyPath = @"Software\Classes\lertaro";

    public static void EnsureRegistered()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
                return;

            using var protocolKey = Registry.CurrentUser.CreateSubKey(ProtocolKeyPath, writable: true);
            protocolKey.SetValue(string.Empty, "URL:Lertaro Protocol", RegistryValueKind.String);
            protocolKey.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);

            using var commandKey = protocolKey.CreateSubKey(@"shell\open\command", writable: true);
            commandKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UrlProtocolManager] Failed to register lertaro:// protocol: {ex.Message}", LogLevel.Warn);
        }
    }
}
