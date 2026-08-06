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
}
