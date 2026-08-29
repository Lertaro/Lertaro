using System.Text.Json;

namespace Lertaro.Core;

// Split out purely to keep UserSettings.cs under the repository's per-file line limit; this class owns
// persistence state and always operates on the UserSettings instance supplied by its caller.
internal static class UserSettingsPersistence
{
    private static readonly Lazy<string> UserDataDirectory = new(() =>
    {
        SettingsDataDirectoryMigrator.Migrate(Logger.UserDataDir, updateUserSettings: true);
        return Logger.UserDataDir;
    });

    public static string SettingsPath => Path.Combine(UserDataDirectory.Value, "user-settings.json");
    private const int BackupCount = 5;
    private static UserSettings? _cachedSettings;
    private static string? _lastJsonOnDisk;
    private static readonly object CacheLock = new();

    public static UserSettings Load()
    {
        lock (CacheLock)
        {
            return _cachedSettings ??= LoadFromDisk();
        }
    }

    public static UserSettings ForceReload()
    {
        lock (CacheLock)
        {
            return _cachedSettings = LoadFromDisk();
        }
    }

    private static UserSettings LoadFromDisk()
    {
        var json = TryReadMainJson();
        var settings = json != null ? TryParse(json) : null;
        if (settings != null)
        {
            lock (CacheLock) _lastJsonOnDisk = json;
            return settings;
        }
        if (json != null)
        {
            settings = UserSettingsBackupStore.TryLoadNewest(SettingsPath, BackupCount, backupJson =>
            {
                var restored = TryParse(backupJson);
                if (restored != null)
                {
                    lock (CacheLock) _lastJsonOnDisk = backupJson;
                }
                return restored;
            });
        }
        return settings ?? new UserSettings();
    }

    private static string? TryReadMainJson()
    {
        if (!File.Exists(SettingsPath)) return null;
        var retries = 5;
        while (true)
        {
            try
            {
                using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException)
            {
                if (--retries <= 0) throw;
                Task.Delay(50).Wait();
            }
        }
    }

    /// <summary>Parses settings JSON with hotkey normalization; null when it cannot be parsed.</summary>
    public static UserSettings? TryParse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            NormalizeHotkeys(settings);
            return settings;
        }
        catch (Exception ex)
        {
            Logger.Log($"[UserSettings] Settings file is corrupt: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    public static void NormalizeHotkeys(UserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Hotkeys.ToggleWindowHotkey))
            settings.Hotkeys.ToggleWindowHotkey = new HotkeyPageSettings().ToggleWindowHotkey;
    }

    public static void Save(UserSettings settings)
    {
        NormalizeHotkeys(settings);
        Directory.CreateDirectory(Logger.UserDataDir);
        lock (CacheLock)
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            if (json == _lastJsonOnDisk) { _cachedSettings = settings; return; }
            RotateBackups(SettingsPath);
            AtomicFileStore.Write(SettingsPath, json);
            _cachedSettings = settings;
            _lastJsonOnDisk = json;
        }
        ExclusionRuleSet.InvalidateCache();
    }

    public static void RotateBackups(string filePath, int maxBackups = 5) => UserSettingsBackupStore.Rotate(filePath, maxBackups);

    public static void RestoreFrom(string sourcePath)
    {
        lock (CacheLock)
        {
            var restored = WriteRestored(sourcePath, SettingsPath, BackupCount, out var json);
            _cachedSettings = restored;
            _lastJsonOnDisk = json;
        }
        ExclusionRuleSet.InvalidateCache();
    }

    public static UserSettings WriteRestored(string sourcePath, string settingsPath, int backupCount, out string json)
    {
        json = File.ReadAllText(sourcePath);
        var restored = TryParse(json)
            ?? throw new InvalidDataException($"The file is not a valid user settings file: {sourcePath}");
        RotateBackups(settingsPath, backupCount);
        AtomicFileStore.Write(settingsPath, json);
        return restored;
    }
}
