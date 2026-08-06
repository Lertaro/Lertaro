using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Services.Network;

public class ResolvedNetworkDrive
{
    public string Letter { get; set; } = string.Empty;
    public string UncPath { get; set; } = string.Empty;
    public bool IsReady { get; set; }
}

public static class NetworkDriveResolver
{
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

    /// <summary>
    /// Returns all network drives known to the current user session.
    /// </summary>
    public static List<ResolvedNetworkDrive> GetNetworkDrives()
    {
        var results = new List<ResolvedNetworkDrive>();

        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Network)
                {
                    var letter = d.Name.Split(':')[0].ToUpperInvariant();

                    results.Add(new ResolvedNetworkDrive
                    {
                        Letter = letter,
                        UncPath = GetUncPath(letter),
                        IsReady = d.IsReady
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkDriveResolver] Failed to get network drives: {ex.Message}", LogLevel.Error);
        }

        return results.OrderBy(d => d.Letter).ToList();
    }

    public static string GetUncPath(string driveLetter)
    {
        var letter = NormalizeDrive(driveLetter);
        if (letter.Length == 0)
            return string.Empty;

        try
        {
            var length = 1024;
            var builder = new StringBuilder(length);
            var result = WNetGetConnection(letter + ":", builder, ref length);
            return result == 0 ? builder.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string GetNetworkId(string driveLetter)
    {
        var unc = GetUncPath(driveLetter);
        return string.IsNullOrWhiteSpace(unc)
            ? string.Empty
            : Indexer.NetworkDrive.NetworkDriveCacheLocator.GetIdForUnc(unc);
    }

    private static string NormalizeDrive(string drive) => string.IsNullOrWhiteSpace(drive)
        ? string.Empty
        : drive.Trim().TrimEnd(':', '\\').ToUpperInvariant();
}
