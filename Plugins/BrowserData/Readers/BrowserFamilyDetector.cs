namespace Lertaro.Plugins.BrowserData.Readers;

internal enum BrowserFamily
{
    Unknown,
    Chromium,
    Firefox
}

// Detects which reader a configured profile directory needs by checking for the files each browser
// family actually keeps directly in a profile folder -- no need for the user to specify which browser
// they added, and it works the same for any Chromium fork (Edge, Brave, Vivaldi, ...) or Firefox fork.
internal static class BrowserFamilyDetector
{
    public static BrowserFamily Detect(string profileDir)
    {
        if (File.Exists(Path.Combine(profileDir, "places.sqlite")))
            return BrowserFamily.Firefox;
        if (File.Exists(Path.Combine(profileDir, "Bookmarks")) || File.Exists(Path.Combine(profileDir, "History")))
            return BrowserFamily.Chromium;
        return BrowserFamily.Unknown;
    }
}
