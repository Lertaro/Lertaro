using Microsoft.Win32;
using System.Security;

namespace Lertaro.Core.Services.Installation;

/// <summary>Identifies the installed copy without relying on its directory's name.</summary>
public static class InstallationDetector
{
    // Inno Setup creates this uninstall key from Installer/installer.iss's fixed AppId.
    private const string UninstallKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{D37D0B75-B5E3-40D9-92EE-429C7D4D7F2A}_is1";

    /// <summary>
    /// Returns <see cref="InstallationMode.Installed"/> only when Inno Setup registered this exact
    /// executable. A copied installation therefore behaves as portable instead of inheriting the
    /// original copy's machine-level state.
    /// </summary>
    public static InstallationMode Detect()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallKeyPath);
            if (key == null)
                return InstallationMode.Portable;

            return key.GetValue("InstallLocation") is string location &&
                   !string.IsNullOrWhiteSpace(location) &&
                   !string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? IsInstalledAt(location, Environment.ProcessPath)
                    ? InstallationMode.Installed
                    : InstallationMode.Portable
                : InstallationMode.Portable;
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            return InstallationMode.Portable;
        }
    }

    internal static bool IsInstalledAt(string installLocation, string executablePath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(Path.Combine(installLocation, Path.GetFileName(executablePath))),
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
