using System.Diagnostics;
using System.Security.Principal;

namespace Lertaro.Core.Services;

public static class ElevationManager
{
    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryElevateProcess(string exePath, string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            if (args != null && args.Length > 0)
            {
                startInfo.Arguments = string.Join(" ", args);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.Log($"[ElevationManager] UAC elevation prompt was declined: {ex.Message}", LogLevel.Info);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ElevationManager] Failed to relaunch elevated: {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
