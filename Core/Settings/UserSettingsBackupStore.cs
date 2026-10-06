namespace Lertaro.Core;

/// <summary>
/// Split from <see cref="UserSettings"/> to keep that settings model under the repository line limit.
/// Rotation must succeed before a save/import may overwrite the current settings.
/// </summary>
internal static class UserSettingsBackupStore
{
    public static void Rotate(string filePath, int maxBackups)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBackups, 1);
        var current = SettingsFileReader.ReadIfPresent(filePath);
        if (current == null) return;
        for (var index = maxBackups - 1; index >= 1; index--)
        {
            var previous = SettingsFileReader.ReadIfPresent($"{filePath}.bak.{index}");
            if (previous != null)
                AtomicFileStore.Write($"{filePath}.bak.{index + 1}", previous);
        }
        // Copy content rather than file attributes/ACLs; leave every source intact on failure.
        AtomicFileStore.Write($"{filePath}.bak.1", current);
    }

    /// <summary>
    /// Returns the settings parsed from the newest intact .bak.N backup (oldest index last), or null
    /// when none parses. Called after the main file failed to read or parse.
    /// </summary>
    internal static UserSettings? TryLoadNewest(string filePath, int maxBackups, Func<string, UserSettings?> tryParse)
    {
        string BackupPath(int index) => $"{filePath}.bak.{index}";

        for (var index = 1; index <= maxBackups; index++)
        {
            var backupPath = BackupPath(index);
            if (!File.Exists(backupPath))
                continue;

            try
            {
                var restored = tryParse(File.ReadAllText(backupPath));
                if (restored != null)
                {
                    Logger.Log($"[UserSettings] Restored settings from backup '{backupPath}' after the main file failed to parse", LogLevel.Warn);
                    return restored;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[UserSettings] Failed to read settings backup '{backupPath}': {ex.Message}", LogLevel.Warn);
            }
        }

        return null;
    }
}
