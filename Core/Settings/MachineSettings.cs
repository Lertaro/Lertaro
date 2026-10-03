using System.Text.Json;

namespace Lertaro.Core;

public class MachineSettings
{
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

    // The walk's exclusion/ignore rules, mirrored from the interactive user's settings. Here rather than
    // only in the per-user file because the local (FAT32/exFAT) drive walk runs in the --service process
    // as LocalSystem: UserSettings there resolves to Data\Users\<that SID's hash>, which does not exist,
    // so the service read defaults and ExcludedPaths never reached the walk at all. The App writes these
    // (see MirrorExclusionRules), the service only reads them -- same split LocalDrives already uses.
    public List<string> ExcludedPaths { get; set; } = new();
    public List<string> IgnoredPathGlobs { get; set; } = new();
    public List<string> IgnoredPathRegexes { get; set; } = new();

    /// <summary>
    /// Copies the walk's three rule lists off the interactive user's settings.
    /// </summary>
    public void SyncExclusionRulesFrom(UserSettings settings)
    {
        ExcludedPaths = settings.ExcludedPaths?.ToList() ?? new();
        IgnoredPathGlobs = settings.IgnoredPathGlobs?.ToList() ?? new();
        IgnoredPathRegexes = settings.IgnoredPathRegexes?.ToList() ?? new();
    }

    private static readonly Lazy<string> SharedDataDirectory = new(() =>
    {
        SettingsDataDirectoryMigrator.Migrate(Logger.SharedDataDir, updateUserSettings: false);
        return Logger.SharedDataDir;
    });

    // One shared instance: a freshly built JsonSerializerOptions re-derives the contract metadata for
    // the whole object graph on every call.
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(SharedDataDirectory.Value, "machine-settings.json");

    private static string BackupPath => SettingsPath + ".bak";

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
        // A missing file is a fresh install and gets defaults; an existing file that cannot be read
        // or parsed falls back to the backup the atomic writer left behind, because returning bare
        // defaults here would read as "no drives configured" and let the next Save() persist them
        // over the real drive selection.
        var settings = File.Exists(SettingsPath) ? TryLoadFromFile(SettingsPath) ?? TryLoadFromFile(BackupPath) : null;
        if (settings == null)
            return CreateDefault();

        settings.LocalDrives = settings.LocalDrives
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.MigrateLegacyLocalDriveSelection(DetectLocalDriveIds());
        return settings;
    }

    /// <summary>
    /// Reads and parses one settings file; null when it is missing or still fails to read or parse.
    /// Per-file parser: it deliberately does not apply the drive-list normalization Load() runs on
    /// the result it settles for.
    /// </summary>
    internal static MachineSettings? TryLoadFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        // The service reads this file while the app atomically replaces it, so a sharing violation is
        // transient: retry a few times before giving up. The filter's decrement is the retry budget;
        // once spent, an IOException falls through to the general handler below and fails over to the
        // backup via the null return.
        var retries = 3;
        while (true)
        {
            try
            {
                return JsonSerializer.Deserialize<MachineSettings>(File.ReadAllText(path)) ?? new MachineSettings();
            }
            catch (IOException) when (retries-- > 0)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                Logger.Log($"[MachineSettings] Failed to load settings from '{path}': {ex.Message}", LogLevel.Error);
                return null;
            }
        }
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

    public void Save()
    {
        Directory.CreateDirectory(Logger.SharedDataDir);
        AtomicFileStore.Write(SettingsPath, JsonSerializer.Serialize(this, WriteOptions), BackupPath);
    }

    /// <summary>
    /// Refreshes the machine copy of the walk's exclusion rules from the interactive user's settings.
    /// </summary>
    /// <remarks>
    /// Called by the App only: on every user-settings save, on a settings restore, and once at startup
    /// (for an install whose machine-settings.json predates these fields, where waiting for the next
    /// settings edit would leave the service filtering by nothing). The service never calls this -- it
    /// cannot read the per-user file, which is exactly why the rules are mirrored here -- and nothing
    /// else may call it, because a write built from anything but the user's own settings would clobber
    /// them with values nobody chose.
    ///
    /// The raw file is read rather than <see cref="Load"/>, deliberately: Load runs the legacy
    /// drive-selection migration, which would stamp the drives detected at this moment into the file as
    /// an explicit selection. A machine file that never listed drives has to keep meaning "every drive
    /// detected at service start", and this write is only ever about the rules.
    /// </remarks>
    public static void MirrorExclusionRules(UserSettings settings) => MirrorExclusionRules(settings, SettingsPath);

    internal static void MirrorExclusionRules(UserSettings settings, string path)
    {
        try
        {
            // An existing file that cannot be read right now (locked by the service's own write, or
            // corrupt) is left untouched rather than replaced from an empty object: that write would
            // drop the drive selection this method knows nothing about. The next save retries.
            var mirror = File.Exists(path) ? TryLoadFromFile(path) : new MachineSettings();
            if (mirror == null)
                return;

            if (SameRules(mirror.ExcludedPaths, settings.ExcludedPaths)
                && SameRules(mirror.IgnoredPathGlobs, settings.IgnoredPathGlobs)
                && SameRules(mirror.IgnoredPathRegexes, settings.IgnoredPathRegexes))
                return;

            mirror.SyncExclusionRulesFrom(settings);
            mirror.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A machine file this process cannot write must not fail the user's own settings save: the
            // service then keeps the previous rules, and the next save (or the next launch) retries.
            Logger.Log($"[MachineSettings] Could not mirror the exclusion rules to '{path}': {ex.Message}", LogLevel.Warn);
        }
    }

    private static bool SameRules(List<string>? left, List<string>? right) =>
        (left ?? new()).SequenceEqual(right ?? new(), StringComparer.OrdinalIgnoreCase);

    // The destination as a parameter, the same shape UserSettingsPersistence.TryPersist uses for the
    // same reason: the mirror has to be exercisable against a temp directory, not the real machine file.
    internal void Save(string path) =>
        AtomicFileStore.Write(path, JsonSerializer.Serialize(this, WriteOptions), path + ".bak");
}
