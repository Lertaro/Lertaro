using Microsoft.Win32;

namespace Lertaro.Core.Services.Installation;

/// <summary>The accounts with a profile on this machine, from ProfileList in the registry.</summary>
public static class UserProfiles
{
    /// <summary>SID to profile folder, for every profile Windows knows about (service accounts included).</summary>
    public static Dictionary<string, string> Read()
    {
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (list is null)
            return profiles;

        foreach (var sid in list.GetSubKeyNames())
        {
            using var profile = list.OpenSubKey(sid);
            // GetValue expands REG_EXPAND_SZ (%SystemDrive%\Users\...) by default.
            if (profile?.GetValue("ProfileImagePath") is string { Length: > 0 } folder)
                profiles[sid] = folder;
        }

        return profiles;
    }

    /// <summary>Whether <paramref name="sid"/> is a real account (local or domain) rather than a built-in service one.</summary>
    public static bool IsAccount(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase);
}
