namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

/// <summary>
/// Which of a subscriber's watched directories a batch of changed directories concerns.
/// </summary>
/// <remarks>
/// Runs where the changes are, not where the watch list is. Changes arrive in the thousands per second;
/// a watch list is a handful of paths that changes when a plugin is loaded or a panel is opened. Sending
/// the small thing to meet the large one is what makes a hit rare enough to be worth reporting at all.
/// </remarks>
public static class WatchedDirectoryMatcher
{
    /// <summary>
    /// The watched directories affected by <paramref name="changedDirectories"/>. Null there means the
    /// change could not be pinned down, and every watched directory is returned: losing precision costs
    /// a re-listing nobody needed, while assuming nothing happened costs a subscriber the change it was
    /// waiting for, and only one of those is recoverable.
    /// </summary>
    public static List<string> Match(IReadOnlyCollection<string> watched, IReadOnlyCollection<string>? changedDirectories)
    {
        if (watched.Count == 0)
            return new List<string>();

        if (changedDirectories == null)
            return watched.ToList();

        var hits = new List<string>();
        foreach (var candidate in watched)
        {
            foreach (var changed in changedDirectories)
            {
                if (Touches(changed, candidate))
                {
                    hits.Add(candidate);
                    break;
                }
            }
        }
        return hits;
    }

    /// <summary>
    /// Whether a change in <paramref name="changedDirectory"/> concerns somebody watching
    /// <paramref name="watched"/>.
    /// </summary>
    /// <remarks>
    /// Either nesting direction counts. The change being inside the watched directory is the obvious
    /// one; the watched directory being inside the change matters because a directory is reported by
    /// its own path when it is itself created, renamed or removed, and somebody watching what was in it
    /// needs to hear that.
    ///
    /// Compared with a trailing separator on both sides, so "D:\Foo" never matches a sibling "D:\FooBar".
    /// </remarks>
    public static bool Touches(string changedDirectory, string watched)
    {
        if (string.IsNullOrEmpty(changedDirectory) || string.IsNullOrEmpty(watched))
            return false;

        var change = WithSeparator(changedDirectory);
        var target = WithSeparator(watched);
        return change.StartsWith(target, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithSeparator(string value)
    {
        var normalized = value.Replace('/', Path.DirectorySeparatorChar);
        return normalized.EndsWith(Path.DirectorySeparatorChar) ? normalized : normalized + Path.DirectorySeparatorChar;
    }
}
