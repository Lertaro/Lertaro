namespace Lertaro.Core.Services.LocalSend;

/// <summary>Converts peer-provided relative names into safe Windows paths for LocalSend folder transfers.</summary>
internal static class LocalSendPathSanitizer
{
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

    private static string FindAvailableName(string candidate)
    {
        if (!File.Exists(candidate))
            return candidate;

        var directory = Path.GetDirectoryName(candidate)!;
        var name = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        var counter = 1;
        string available;
        do
        {
            available = Path.Combine(directory, $"{name} ({counter++}){extension}");
        } while (File.Exists(available));
        return available;
    }
}
