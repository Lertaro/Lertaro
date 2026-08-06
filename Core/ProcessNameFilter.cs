namespace Lertaro.Core;

/// <summary>
/// Matches a process against a user-written list of process names. Written as "chrome" or "chrome.exe"
/// either way, because both are what people type and the two sources this compares against disagree
/// themselves: the image path gives "chrome.exe" while <c>Process.ProcessName</c> gives "chrome".
/// </summary>
public static class ProcessNameFilter
{
    public static bool Matches(string? processName, IEnumerable<string>? names)
    {
        if (string.IsNullOrEmpty(processName) || names == null)
            return false;

        var bare = StripExe(processName);
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name) && StripExe(name.Trim()).Equals(bare, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string StripExe(string value)
        => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value.Substring(0, value.Length - 4) : value;
}
