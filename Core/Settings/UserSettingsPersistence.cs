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

    /// <summary>
    /// One shared instance: a freshly constructed <see cref="JsonSerializerOptions"/> carries no cached
    /// contract metadata, so every save would re-derive the serializer for the whole settings graph.
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

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
        var settings = LoadFromPath(SettingsPath, out var json);
        _lastJsonOnDisk = json;
        return settings;
    }

    internal static UserSettings LoadFromPath(string path, out string? persistedJson)
    {
        var json = SettingsFileReader.ReadIfPresent(path);
        var settings = json == null ? null : TryParse(json);
        var hasPersistedData = json != null;
        persistedJson = json;
        if (settings != null)
            return settings;
        for (var index = 1; index <= BackupCount; index++)
        {
            var backup = SettingsFileReader.ReadIfPresent($"{path}.bak.{index}");
            if (backup == null) continue;
            hasPersistedData = true;
            settings = TryParse(backup);
            if (settings == null) continue;
            // The primary still needs restoring, even when the next save changes no preferences.
            persistedJson = null;
            return settings;
        }
        return !hasPersistedData ? new UserSettings()
            : throw new InvalidDataException($"No valid user settings remain at '{path}'. Restore a backup before saving.");
    }

    /// <summary>Parses settings JSON with hotkey normalization; null when it cannot be parsed.</summary>
    public static UserSettings? TryParse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(json);
            if (settings is null) return null;
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

    /// <summary>
    /// Writes <paramref name="settings"/> to <see cref="SettingsPath"/>. Returns false when the write
    /// failed: the reason is logged and the cache is left pointing at what is actually on disk, so
    /// <see cref="Load"/> never reports a change nothing persisted, and the caller's next change retries.
    /// </summary>
    public static bool Save(UserSettings settings)
    {
        NormalizeHotkeys(settings);
        lock (CacheLock)
        {
            var json = JsonSerializer.Serialize(settings, WriteOptions);
            // Persist even an unchanged object: another process may have removed/replaced the file.
            if (!TryPersist(json, SettingsPath))
            {
                _cachedSettings = _lastJsonOnDisk == null ? null : TryParse(_lastJsonOnDisk);
                return false;
            }
            _cachedSettings = settings;
            _lastJsonOnDisk = json;
        }
        ExclusionRuleSet.InvalidateCache();
        return true;
    }

    internal static bool TryPersist(string json, string settingsPath)
    {
        try
        {
            RotateBackups(settingsPath);
            AtomicFileStore.Write(settingsPath, json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"[UserSettings] Save failed, settings on disk left unchanged: {ex.Message}", LogLevel.Error);
            return false;
        }
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
