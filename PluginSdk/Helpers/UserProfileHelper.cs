using System.IO;

namespace Lertaro.PluginSdk.Helpers;

public static class UserProfileHelper
{
    public static List<string> GetAllUserProfilePaths()
    {
        var paths = new List<string>();
        string? profilesDir = null;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (key != null)
            {
                // Read global Public profile path configured in registry
                var publicPath = key.GetValue("Public") as string;
                if (!string.IsNullOrEmpty(publicPath))
                {
                    var fullPublic = Environment.ExpandEnvironmentVariables(publicPath);
                    if (Directory.Exists(fullPublic))
                    {
                        paths.Add(Path.GetFullPath(fullPublic));
                    }
                }

                // Read global Default profile path configured in registry
                var defaultPath = key.GetValue("Default") as string;
                if (!string.IsNullOrEmpty(defaultPath))
                {
                    var fullDefault = Environment.ExpandEnvironmentVariables(defaultPath);
                    if (Directory.Exists(fullDefault))
                    {
                        var resolvedDefault = Path.GetFullPath(fullDefault);
                        if (!paths.Contains(resolvedDefault, StringComparer.OrdinalIgnoreCase))
                        {
                            paths.Add(resolvedDefault);
                        }
                    }
                }

                // Read ProfilesDirectory for fallback use
                var profilesDirVal = key.GetValue("ProfilesDirectory") as string;
                if (!string.IsNullOrEmpty(profilesDirVal))
                {
                    profilesDir = Environment.ExpandEnvironmentVariables(profilesDirVal);
                }

                // Enumerate user SID profiles
                foreach (var subkeyName in key.GetSubKeyNames())
                {
                    using var subkey = key.OpenSubKey(subkeyName);
                    if (subkey != null)
                    {
                        var path = subkey.GetValue("ProfileImagePath") as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            var fullPath = Environment.ExpandEnvironmentVariables(path);
                            if (Directory.Exists(fullPath))
                            {
                                var resolved = Path.GetFullPath(fullPath);
                                if (!paths.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                                {
                                    paths.Add(resolved);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserProfileHelper] Failed to read ProfileList from registry: {ex.Message}", LogLevel.Warn);
        }

        // Fallback to user profiles directory if registry query returned nothing
        if (paths.Count == 0)
        {
            var usersRoot = profilesDir;
            if (string.IsNullOrEmpty(usersRoot))
            {
                usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users");
            }

            if (Directory.Exists(usersRoot))
            {
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(usersRoot))
                    {
                        paths.Add(Path.GetFullPath(dir));
                    }
                }
                catch { }
            }
        }

        return paths;
    }

    public static string GetDesktopPath(string profilePath) => Path.Combine(profilePath, "Desktop");

    public static string GetStartMenuPath(string profilePath) => Path.Combine(profilePath, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu");
}
