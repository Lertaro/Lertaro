namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

// A registration's FilterPattern, in the "*.exe;*.lnk" form plugins register it in: split into the
// single patterns Directory.EnumerateFiles accepts one at a time, and matched with the same Win32
// wildcard semantics that call would apply, so an index-backed enumeration and a live filesystem walk
// of the same directory agree on which names the pattern selects.
public static class FilterPatternHelper
{
    public static string[] Split(string filterPattern)
    {
        if (string.IsNullOrWhiteSpace(filterPattern)) return new[] { "*" };
        var patterns = filterPattern.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return patterns.Length > 0 ? patterns : new[] { "*" };
    }

    // null = "everything matches", so a caller enumerating a whole subtree can skip per-name matching
    // outright instead of running a wildcard match that can only ever return true.
    public static string[]? SplitOrNullIfMatchAll(string? filterPattern)
    {
        var patterns = Split(filterPattern ?? string.Empty);
        return patterns.Any(IsMatchAll) ? null : patterns;
    }

    /// <summary>Combines registrations for one directory without losing either caller's file scope.</summary>
    public static string Combine(string existing, string incoming)
    {
        var patterns = Split(existing)
            .Concat(Split(incoming))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return patterns.Any(IsMatchAll) ? "*" : string.Join(';', patterns);
    }

    public static bool Matches(string name, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            // Translated first, exactly as Directory.EnumerateFiles does before matching
            // (FileSystemEnumerableFactory.NormalizeInputs): that is what turns "*.*" into "everything"
            // and the trailing dot of "*." into the DOS wildcard meaning "no extension". Matching the
            // raw expression would read both literally and quietly disagree with a live walk of the
            // same directory. IsMatchAll stays as a fast path for the pattern nearly everyone uses.
            if (IsMatchAll(pattern)
                || System.IO.Enumeration.FileSystemName.MatchesWin32Expression(
                    System.IO.Enumeration.FileSystemName.TranslateWin32Expression(pattern), name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static bool IsMatchAll(string pattern) => pattern is "*" or "*.*";
}
