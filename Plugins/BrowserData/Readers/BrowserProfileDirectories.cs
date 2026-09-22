using System.IO;

namespace Lertaro.Plugins.BrowserData.Readers;

/// <summary>
/// Turns one configured folder into the browser profile folders to actually read.
/// </summary>
/// <remarks>
/// A path the user points at is usually the folder that *holds* profiles (Chromium's <c>User Data</c>,
/// Firefox's <c>Profiles</c>) rather than a single profile, and for Firefox it cannot be anything else:
/// that profile folder's name is a random token assigned per install. Pointing straight at one profile
/// is equally supported, so a pre-existing setup keeps working untouched either way.
/// </remarks>
internal static class BrowserProfileDirectories
{
    /// <summary>
    /// The profile folders inside <paramref name="configuredDir"/>, or that folder itself when it is
    /// already one. Anything it cannot recognise (<c>Crashpad</c>, <c>Safe Browsing</c>, Firefox's
    /// <c>Pending</c> stubs, the shader/gpu cache folders Chromium sits alongside its profiles) is left
    /// out silently rather than logged, since a <c>User Data</c> folder holds a lot of those.
    /// </summary>
    public static List<string> Discover(string configuredDir)
    {
        if (BrowserFamilyDetector.Detect(configuredDir) != BrowserFamily.Unknown)
            return [configuredDir];

        var found = new List<string>();
        try
        {
            foreach (var child in Directory.EnumerateDirectories(configuredDir))
            {
                if (BrowserFamilyDetector.Detect(child) != BrowserFamily.Unknown)
                    found.Add(child);
            }
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Couldn't list profiles in '{configuredDir}': {ex.Message}", PluginSdk.LogLevel.Warn);
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }
}
