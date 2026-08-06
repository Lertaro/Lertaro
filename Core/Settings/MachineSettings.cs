using System.Text.Json;

namespace Lertaro.Core;

public class MachineSettings
{
    public List<string> LocalDrives { get; set; } = new();

    /// <summary>
    /// How much the background service writes to service.log: Error, Warn, Info (the default) or Debug.
    /// </summary>
    /// <remarks>
    /// Here rather than in the per-user settings, because the service is the one process that cannot
    /// read those: it runs as LocalSystem, and UserSettings lives under the interactive user's
    /// %LocalAppData%. That is why the service had no configurable level at all -- App and the hook both
    /// set Logger.MinimumLevel from the user setting on startup, and the --service branch never had
    /// anything to read, so every LogLevel.Debug line in the indexer was unreachable no matter what the
    /// settings page said. The USN layer's own diagnostics live at that level.
    ///
    /// No settings page: this is a diagnostic dial, edited by hand in machine-settings.json when
    /// somebody is actually looking, and left alone otherwise. Info by default, matching what the app
    /// and the hook run at -- the service's log is the one place a problem in the indexer shows up, and
    /// a level below Info would leave a machine nobody has touched yet with nothing to go on.
    /// </remarks>
    public string ServiceLogLevel { get; set; } = "Info";

    private static readonly Lazy<string> SharedDataDirectory = new(() =>
    {
        SettingsDataDirectoryMigrator.Migrate(Logger.SharedDataDir, updateUserSettings: false);
        return Logger.SharedDataDir;
    });

    public static string SettingsPath => Path.Combine(SharedDataDirectory.Value, "machine-settings.json");

    /// <summary>
    /// <see cref="ServiceLogLevel"/> as a level, defaulting to Info for anything unrecognised.
    /// </summary>
    /// <remarks>
    /// Case-insensitive and forgiving on purpose: this file is edited by hand, and "debug" failing
    /// silently back would look exactly like the level having no effect -- which is the very symptom
    /// that made this setting necessary.
    ///
    /// Something written but not understood lands on the same Info a file that never mentioned it gets:
    /// a value nobody recognises is a typo, and answering a typo by going quiet would hide the mistake
    /// behind a silence indistinguishable from a deliberate "Error".
    /// </remarks>
    public LogLevel ResolveServiceLogLevel() => ServiceLogLevel?.Trim().ToLowerInvariant() switch
    {
        "error" => LogLevel.Error,
        "warn" => LogLevel.Warn,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info
    };

    public static MachineSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new MachineSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<MachineSettings>(json) ?? new MachineSettings();
            settings.LocalDrives = settings.LocalDrives
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return settings;
        }
        catch (Exception ex)
        {
            Logger.Log($"[MachineSettings] Failed to load settings: {ex.Message}", LogLevel.Error);
            return new MachineSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Logger.SharedDataDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
    }
}
