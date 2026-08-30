using System.Text.RegularExpressions;

namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Runtime configuration snapshot for the content indexing engine.
/// </summary>
public sealed class ContentIndexConfig
{
    public IReadOnlyList<string> MonitoredFolders { get; init; } = Array.Empty<string>();
    public long MaxFileSizeBytes { get; init; } = long.MaxValue;
    public HashSet<string> AllowedExtensions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hard cap on the index database size; once exceeded, new files are not indexed
    /// until the cap is raised or the index is cleared. 0 means unlimited.
    /// </summary>
    public long MaxIndexSizeBytes { get; init; } = 5L * 1024 * 1024 * 1024;

    // The exclusion blacklist: user-supplied full-path regexes compiled once per config
    // snapshot. Matching runs against the whole path string, so a pattern that matches an
    // ancestor folder also matches everything below it, which is how whole subtrees drop out.
    public IReadOnlyList<Regex> ExcludedPatterns { get; init; } = Array.Empty<Regex>();

    public string FilterPattern => AllowedExtensions.Count > 0
        ? string.Join(";", AllowedExtensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)))
        : string.Empty;

    public bool IsAllowedExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return false;
        return AllowedExtensions.Contains(ext);
    }

    /// <summary>
    /// True when the file or any of its ancestor folders matches an exclusion pattern;
    /// such files are neither indexed nor kept in the index.
    /// </summary>
    public bool IsExcluded(string filePath)
    {
        foreach (var pattern in ExcludedPatterns)
        {
            try
            {
                if (pattern.IsMatch(filePath))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological pattern must never stall discovery; treat a timeout as
                // "not excluded" so indexing keeps flowing.
            }
        }
        return false;
    }

    /// <summary>
    /// Parses the user's semicolon-separated exclusion regexes. Invalid entries are
    /// skipped with a warning instead of disabling indexing wholesale.
    /// ponytail: matching is plain IsMatch against the full path; anchoring (e.g.
    /// "C:\\Temp\\.*\.tmp$") is the pattern author's responsibility, and the 200 ms
    /// match timeout is the guard against catastrophic backtracking on user input.
    /// </summary>
    public static IReadOnlyList<Regex> ParseExcludedPatterns(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<Regex>();

        var patterns = new List<Regex>();
        foreach (var part in raw.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                patterns.Add(new Regex(part, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)));
            }
            catch (ArgumentException ex)
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Ignoring invalid exclusion pattern '{part}': {ex.Message}",
                    PluginSdk.LogLevel.Warn);
            }
        }
        return patterns;
    }
}
