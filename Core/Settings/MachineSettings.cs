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

    // The copy another process handed this one, when it cannot read the file itself. See Serve.
    private static volatile MachineSettings? _served;

    /// <summary>
    /// Takes the machine settings from whoever owns the file, for a process that cannot read it.
    /// </summary>
    /// <remarks>
    /// On a portable install the service locks <c>Data\Machine</c> for itself (see PortableDirectoryLock),
    /// and its own comment says why: it writes that directory as LocalSystem, and until the directory is
    /// locked any user could have planted a link in it that redirects exactly that write. The consequence
    /// is that the App -- running as the interactive user -- cannot read machine-settings.json at all. It
    /// is not a permission problem: the ACL still grants Users read, and it is the service's held
    /// directory handle that refuses, so no grant can fix it from this side.
    ///
    /// So the App asks the service over the pipe (<c>GetMachineSettingsAsync</c>, which calls this) and
    /// hands the answer here; <see cref="Load"/> then answers with what the process that DOES own the file
    /// said. Left unset in the service, and in any process whose own read works, where Load reads the file
    /// as it always has.
    ///
    /// Pass null to forget a copy -- the seam a test needs, and the honest state for a process whose
    /// service went away.
    ///
    /// ponytail: one copy per process, refreshed when the pipe is asked or told, so a selection the
    /// service changes on its own (a legacy-selection migration, say) stays stale here until the next
    /// fetch. The alternative is a change notification over the pipe, which is more machinery than a
    /// setting nobody edits outside the App's own settings page needs today.
    /// </remarks>
    public static void Serve(MachineSettings? settings) => _served = settings;

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
        // Told, rather than read: see Serve. This is the whole of what a process the service has locked
        // out of Data\Machine can know about the machine, and it is the same object the pipe handed over,
        // so a caller cannot tell the two apart.
        if (_served is { } served)
            return served;

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
}
