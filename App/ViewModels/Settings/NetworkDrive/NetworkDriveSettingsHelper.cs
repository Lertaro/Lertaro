using Lertaro.Core;
using Lertaro.Core.Indexer.NetworkDrive;
using Lertaro.App.Services;

using Lertaro.Core.Services.Network;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

internal static class NetworkDriveSettingsHelper
{
    public static string GetStateText(ResolvedNetworkDrive? drive, NetworkIndexStatus? indexStatus)
    {
        if (drive != null && !drive.IsReady)
            return TranslationManager.Instance["Network_StatusUnavailable"];

        return indexStatus?.State switch
        {
            "indexing" => TranslationManager.Instance["Network_StatusIndexing"],
            "ready" => TranslationManager.Instance["Network_StatusReady"],
            "cached" => TranslationManager.Instance["Network_StatusCached"],
            "error" => TranslationManager.Instance["Network_StatusError"],
            "pending" => TranslationManager.Instance["Network_StatusPending"],
            _ => TranslationManager.Instance["Network_StatusConnected"]
        };
    }


    public static List<string> GetWslDistros()
    {
        var distros = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    var distroName = subKey?.GetValue("DistributionName") as string;
                    if (!string.IsNullOrEmpty(distroName))
                    {
                        distros.Add(distroName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveSettings] Failed to scan WSL distributions via registry: {ex.Message}", LogLevel.Warn);
        }
        return distros;
    }

    public static string NormalizeRefreshMode(string? refreshMode) => refreshMode switch
    {
        "15Minutes" => "15Minutes",
        "Hourly" => "Hourly",
        "Daily" => "Daily",
        _ => "Manual"
    };

    // Extracts the distro name from a WSL UNC cache key, e.g. "\\wsl$\Ubuntu" -> "Ubuntu". Deliberately
    // NOT System.IO.Path.GetFileName: a bare two-segment UNC path has no path component past its root by
    // .NET's own path rules (same reason Path.GetFileName(@"C:\") == ""), so it always returns "" here
    // instead of the distro name -- confirmed by a real MSTest run, not just reasoning about the API.
    public static string GetWslDistroName(string uncPath) => uncPath.TrimEnd('\\').Split('\\').Last();
}
