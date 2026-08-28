namespace Lertaro.Core;

/// <summary>
/// Split from <see cref="UserSettings"/> to keep that settings model under the repository line limit.
/// This class owns no state and performs the same best-effort backup rotation for its caller.
/// </summary>
internal static class UserSettingsBackupStore
{
    public static void Rotate(string filePath, int maxBackups)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            string BackupPath(int index) => $"{filePath}.bak.{index}";

            var oldest = BackupPath(maxBackups);
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (var index = maxBackups - 1; index >= 1; index--)
            {
                var source = BackupPath(index);
                if (File.Exists(source))
                    File.Move(source, BackupPath(index + 1));
            }

            File.Copy(filePath, BackupPath(1), overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserSettings] Failed to rotate settings backups for '{filePath}': {ex.Message}", LogLevel.Warn);
        }
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
