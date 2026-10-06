using System.Text.Json;

namespace Lertaro.Core;

public class MachineSettings
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }
    public List<string> LocalDrives { get; set; } = new();

    // Older settings files used an empty LocalDrives list to mean "all drives". This persisted marker
    // distinguishes those files from a user explicitly clearing every checkbox under the new semantics.
    public bool LocalDriveSelectionConfigured { get; set; }

    public bool IsLocalDriveEnabled(string? volumeId) =>
        !string.IsNullOrWhiteSpace(volumeId) && LocalDrives.Contains(volumeId, StringComparer.OrdinalIgnoreCase);

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


    // One shared instance: a freshly built JsonSerializerOptions re-derives the contract metadata for
    // the whole object graph on every call.
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(Logger.SharedDataDir, "machine-settings.json");


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

    public static MachineSettings Load() => Load(SettingsPath);

    internal static MachineSettings Load(string path)
    {
        // A missing file is a fresh install and gets defaults; an existing file that cannot be read
        // or parsed falls back to the backup the atomic writer left behind, because returning bare
        // defaults here would read as "no drives configured" and let the next Save() persist them
        // over the real drive selection.
        var json = SettingsFileReader.ReadIfPresent(path);
        var settings = json == null ? null : Parse(json);
        var backupJson = settings == null ? SettingsFileReader.ReadIfPresent(path + ".bak") : null;
        settings ??= backupJson == null ? null : Parse(backupJson);
        if (settings == null)
            return json == null && backupJson == null ? CreateDefault()
                : throw new InvalidDataException($"No readable, valid machine settings remain at '{path}'.");

        settings.LocalDrives = settings.LocalDrives
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.MigrateLegacyLocalDriveSelection(DetectLocalDriveIds());
        return settings;
    }

    /// <summary>
    /// Reads one settings file; null when missing or malformed. Access and I/O failures propagate.
    /// Per-file parser: it deliberately does not apply the drive-list normalization Load() runs on
    /// the result it settles for.
    /// </summary>
    internal static MachineSettings? TryLoadFromFile(string path)
    {
        var json = SettingsFileReader.ReadIfPresent(path);
        return json == null ? null : Parse(json);
    }

    private static MachineSettings? Parse(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<MachineSettings>(json);
            return settings?.LocalDrives == null ? null : settings;
        }
        catch (JsonException) { return null; }
    }

    internal void MigrateLegacyLocalDriveSelection(IEnumerable<string> detectedVolumeIds)
    {
        if (LocalDriveSelectionConfigured)
            return;

        if (LocalDrives.Count == 0)
            LocalDrives = detectedVolumeIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        LocalDriveSelectionConfigured = true;
    }

    private static MachineSettings CreateDefault()
    {
        var settings = new MachineSettings();
        settings.MigrateLegacyLocalDriveSelection(DetectLocalDriveIds());
        return settings;
    }

    private static IEnumerable<string> DetectLocalDriveIds() => VolumeHelper.DetectIndexableLocalDrives()
        .Select(VolumeHelper.GetVolumeId)
        .OfType<string>()
        .Where(id => !string.IsNullOrWhiteSpace(id));

    public void Save() => Save(SettingsPath);

    internal void Save(string path) => AtomicFileStore.Write(path, JsonSerializer.Serialize(this, WriteOptions), path + ".bak");
}
