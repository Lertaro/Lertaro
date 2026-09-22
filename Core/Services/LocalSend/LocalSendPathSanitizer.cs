namespace Lertaro.Core.Services.LocalSend;

/// <summary>Converts peer-provided relative names into safe Windows paths for LocalSend folder transfers.</summary>
internal static class LocalSendPathSanitizer
{
    // Ceiling on the " (n)" suffix search. Real folders collide a handful of times at most; the cap
    // exists so a directory that cannot be written to at all fails fast instead of looping.
    private const int MaxNameAttempts = 1000;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    internal static string? Resolve(string downloadDirectory, string rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName) || Path.IsPathRooted(rawFileName.Replace('/', '\\')))
            return null;

        var components = rawFileName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (components.Any(component => component == ".."))
            return null;

        var safeComponents = components
            .Where(component => component != ".")
            .Select(SanitizeComponent)
            .ToArray();
        if (safeComponents.Length == 0 || safeComponents.Any(string.IsNullOrEmpty))
            return null;

        var root = Path.GetFullPath(downloadDirectory);
        Directory.CreateDirectory(root);
        var candidate = Path.GetFullPath(Path.Combine([root, .. safeComponents]));
        var rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(candidate) ?? root);
        return FindAvailableName(candidate);
    }

    internal static string SanitizeComponent(string component)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(component.Select(character =>
            char.IsControl(character) || invalid.Contains(character) ? '_' : character).ToArray()).TrimEnd(' ', '.');
        if (string.IsNullOrEmpty(cleaned))
            return "_";

        var baseName = cleaned.Split('.')[0];
        return ReservedNames.Contains(baseName) ? $"_{cleaned}" : cleaned;
    }

    /// <summary>
    /// Picks a name for the incoming file and reserves it. The reservation is part of the choice: a
    /// File.Exists check followed by the writer's FileMode.Create left a window in which anything else
    /// could land at the chosen path -- the user's own activity in the download folder, or this session's
    /// second parallel upload worker -- and be truncated without a word.
    /// </summary>
    private static string FindAvailableName(string candidate)
    {
        // A directory sitting at the target name stays as-is: that transfer has always failed in the
        // writer, and the receive UI reports the failure. Suffixing around it would quietly land a file
        // next to the folder the sender meant to replace.
        if (Directory.Exists(candidate))
            return candidate;

        if (TryReserve(candidate))
            return candidate;

        var directory = Path.GetDirectoryName(candidate)!;
        var name = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var counter = 1; counter <= MaxNameAttempts; counter++)
        {
            var available = Path.Combine(directory, $"{name} ({counter}){extension}");
            if (TryReserve(available))
                return available;
        }

        // Every suffix up to the cap was taken or unreservable. Hand back a name one past the cap, which
        // nothing has claimed: the writer either creates it or fails on its own, rather than overwriting
        // something the loop could not reserve.
        return Path.Combine(directory, $"{name} ({MaxNameAttempts + 1}){extension}");
    }

    /// <summary>
    /// Creates the file exclusively, so the name is this transfer's from the moment it is chosen rather
    /// than a path someone else may fill before the writer opens it.
    /// </summary>
    private static bool TryReserve(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
