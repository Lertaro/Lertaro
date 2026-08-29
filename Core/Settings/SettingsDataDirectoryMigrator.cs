namespace Lertaro.Core;

/// <summary>
/// Moves persisted settings data from the former product directory without overwriting files created
/// by the current product before settings have first been loaded.
/// </summary>
internal static class SettingsDataDirectoryMigrator
{
    private const string LegacyProductName = "SwiftList";

    public static void Migrate(string currentDirectory, bool updateUserSettings)
    {
        var parentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        if (parentDirectory is null)
            return;

        var legacyDirectory = Path.Combine(parentDirectory, LegacyProductName);
        try
        {
            if (!Directory.Exists(legacyDirectory))
                return;

            if (!Directory.Exists(currentDirectory))
                Directory.Move(legacyDirectory, currentDirectory);
            else
                MergeDirectory(legacyDirectory, currentDirectory);

            if (updateUserSettings)
                UpdateUserSettings(currentDirectory, legacyDirectory);
        }
        catch
        {
            // A later settings load can retry when the current process has the required directory access.
        }
    }

    private static void MergeDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (var sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory));
            if (!Directory.Exists(destinationSubdirectory))
                Directory.Move(sourceSubdirectory, destinationSubdirectory);
            else
                MergeDirectory(sourceSubdirectory, destinationSubdirectory);
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Move(sourceFile, FindAvailablePath(destinationFile));
        }

        Directory.Delete(sourceDirectory);
    }

    private static string FindAvailablePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var extension = Path.GetExtension(path);
        var basePath = extension.Length == 0 ? path : path[..^extension.Length];
        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{basePath}.legacy-{suffix}{extension}";
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static void UpdateUserSettings(string directory, string legacyDirectory)
    {
        var settingsPath = Path.Combine(directory, "user-settings.json");
        if (!File.Exists(settingsPath))
            return;

        var json = File.ReadAllText(settingsPath);
        // Rewrite only the legacy data-directory path, in its raw and JSON string-escaped backslash
        // forms, instead of the bare product name: a blind word replace corrupts legitimate user
        // strings that merely contain "SwiftList" (favorite names, plugin titles, ...). ponytail:
        // forward-slash or \u005c escape variants of the legacy path are not rewritten -- the app only
        // ever wrote backslash paths, so nothing in a settings file it produced needs them.
        var updatedJson = json
            .Replace(legacyDirectory.Replace("\\", "\\\\"), directory.Replace("\\", "\\\\"), StringComparison.Ordinal)
            .Replace(legacyDirectory, directory, StringComparison.Ordinal);
        if (updatedJson != json)
            AtomicFileStore.Write(settingsPath, updatedJson);
    }
}
