using System.IO;
using Lertaro.Plugins.BrowserData.Readers;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData;

internal sealed class ProfileEntries
{
    public required BrowserProfileConfig Profile { get; init; }
    public BrowserFamily Family { get; init; }
    public List<BrowserEntry> Bookmarks { get; init; } = new();
    public List<BrowserEntry> History { get; init; } = new();
}

// Loads and caches every configured profile's bookmarks/history in memory. IInstantResultProvider.
// GetInstantResults runs synchronously on the UI thread per keystroke, so parsing JSON/querying SQLite
// can never happen inline there -- reloads run on a background thread, triggered by a config-signature
// change (mirrors FileFiltersSearchableItemProvider's own reload-on-config-change check) or a coarse
// staleness timer (history keeps growing while the user browses), and the snapshot swaps atomically
// once ready. A query in flight during a reload just keeps using the previous snapshot; there's no
// user-visible "loading" state, matching how other cached providers in this codebase behave.
internal static class BrowserDataCache
{
    private const string PluginDllName = "Lertaro.Plugins.BrowserData.dll";
    private const string ComponentType = "InstantProvider";
    private const string ComponentName = "BrowserDataInstantProvider";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(10);
    private static readonly object Lock = new();
    private static List<ProfileEntries> _snapshot = new();
    private static string _lastSignature = string.Empty;
    private static DateTime _lastLoadUtc = DateTime.MinValue;
    private static bool _loading;

    private static readonly string[] MonitoredFileNames =
    [
        "Bookmarks", "History", "History-wal", "Favicons", "Favicons-wal", "places.sqlite", "places.sqlite-wal",
        "favicons.sqlite", "favicons.sqlite-wal"
    ];

    internal static bool IsComponentEnabled => PluginSettingsService.IsComponentEnabled(
        PluginDllName, ComponentType, ComponentName);

    public static IReadOnlyList<ProfileEntries> GetSnapshot()
    {
        if (!IsComponentEnabled)
            return Array.Empty<ProfileEntries>();

        MaybeTriggerReload();
        lock (Lock)
        {
            return _snapshot;
        }
    }

    // Called once at plugin load time (see BrowserDataInstantProvider's IWarmupable) so the first real
    // "bb <query>" of the session doesn't land on a still-empty snapshot -- same reload path GetSnapshot
    // already uses, just triggered proactively instead of waiting for the first query.
    public static void Preload() => MaybeTriggerReload();

    private static void MaybeTriggerReload()
    {
        if (!IsComponentEnabled)
            return;

        // A plugin's runtime read only ever sees what was actually stored, while the Settings page renders
        // an untouched field from the schema's DefaultValue. Two sources for one list meant a fresh install
        // showed pre-filled browser rows that indexed nothing, because nothing had been stored and this
        // came back null; BrowserDataDefaults.Profiles() is the same list both sides now use. Deleting
        // every row stays distinguishable from never having set it, since that stores an empty list.
        var configured = PluginSettingsService.GetSetting<List<BrowserProfileConfig>>("Lertaro.Plugins.BrowserData", "Profiles", null!)
            ?? BrowserDataDefaults.Profiles();
        var indexBookmarks = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", "IndexBookmarks", true);
        var indexHistory = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", "IndexHistory", true);
        var blacklist = BrowserEntryFilter.NormalizeBlacklist(PluginSettingsService.GetSetting(
            "Lertaro.Plugins.BrowserData", "Blacklist", new List<string>()));
        // Bookmarks/history toggles folded into the same reload signature as Profiles -- flipping either
        // one should take effect on the next query, not wait for the up-to-10-minute staleness timer.
        var signature = System.Text.Json.JsonSerializer.Serialize(configured)
            + $"|{indexBookmarks}|{indexHistory}|{System.Text.Json.JsonSerializer.Serialize(blacklist)}";

        var isConfigChanged = signature != _lastSignature;
        var isStale = DateTime.UtcNow - _lastLoadUtc > RefreshInterval;
        if (!isConfigChanged && !isStale)
            return;

        if (!isConfigChanged && !HaveProfileFilesChanged(configured, _lastLoadUtc))
        {
            _lastLoadUtc = DateTime.UtcNow;
            return;
        }

        lock (Lock)
        {
            if (_loading)
                return;
            _loading = true;
        }

        _lastSignature = signature;
        _lastLoadUtc = DateTime.UtcNow;

        Task.Run(() =>
        {
            try
            {
                var loaded = LoadAll(configured, indexBookmarks, indexHistory, blacklist);
                lock (Lock)
                {
                    _snapshot = loaded;
                }
                MemoryMaintenanceService.RequestTrim();
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log($"[BrowserData] Reload failed: {ex.Message}", PluginSdk.LogLevel.Error);
            }
            finally
            {
                lock (Lock)
                {
                    _loading = false;
                }
            }
        });
    }

    internal static bool HaveProfileFilesChanged(List<BrowserProfileConfig>? profiles, DateTime lastLoadUtc)
    {
        if (lastLoadUtc == DateTime.MinValue)
            return true;

        var expanded = ExpandProfiles(profiles);
        if (expanded.Count == 0)
            return true;

        foreach (var profile in expanded)
        {
            foreach (var fileName in MonitoredFileNames)
            {
                var filePath = Path.Combine(profile.Path, fileName);
                try
                {
                    if (File.Exists(filePath) && File.GetLastWriteTimeUtc(filePath) > lastLoadUtc)
                        return true;
                }
                catch { }
            }
        }

        return false;
    }

    /// <summary>
    /// Expands the configured rows to one row per profile folder actually worth reading: a row pointing
    /// straight at a profile passes through as it is, one pointing at a folder that holds several
    /// (Chromium's <c>User Data</c>, Firefox's <c>Profiles</c>) becomes a row for each profile inside it.
    /// </summary>
    /// <remarks>
    /// This is what makes the shipped defaults usable at all -- Firefox names each profile folder after a
    /// random token, so no pre-filled path could have named one in advance. Reused by
    /// <see cref="HaveProfileFilesChanged"/> so the staleness probe looks at the same folders the load
    /// reads, rather than only at a parent folder that holds no data files of its own.
    /// </remarks>
    private static List<BrowserProfileConfig> ExpandProfiles(List<BrowserProfileConfig>? profiles)
    {
        var expanded = new List<BrowserProfileConfig>();
        if (profiles == null)
            return expanded;

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Path))
                continue;

            // %LOCALAPPDATA%-style Windows env vars (and shell virtual folders), resolved here (never
            // stored resolved) so the defaults can point at a fixed browser install location without
            // baking in a specific username, and so the settings UI keeps showing the readable
            // "%LOCALAPPDATA%\..." form rather than one particular machine's absolute path.
            var configuredDir = UserPathResolver.Resolve(profile.Path);
            if (!Directory.Exists(configuredDir))
                continue;

            var found = BrowserProfileDirectories.Discover(configuredDir);
            if (found.Count == 0)
            {
                PluginSdk.Logger.Log($"[BrowserData] No Chrome/Edge/Firefox profile folder found in '{configuredDir}', skipping.", PluginSdk.LogLevel.Warn);
                continue;
            }

            foreach (var profileDir in found)
            {
                expanded.Add(new BrowserProfileConfig
                {
                    Name = ProfileLabel(profile.Name, profileDir, found.Count),
                    Icon = profile.Icon,
                    Path = profileDir,
                });
            }
        }

        return expanded;
    }

    // The name a result row shows as its source. Left untouched when a configured folder held a single
    // profile -- "Chrome" alone is what the user configured and reads best. With several, the folder's
    // own name has to join it or the two profiles' rows become indistinguishable.
    private static string ProfileLabel(string configuredName, string profileDir, int profileCount)
    {
        if (profileCount == 1)
            return configuredName;

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(profileDir));
        // Firefox spells a profile folder "<random token>.<name>", where name is what whoever created it
        // typed. The token is noise, so "ydcqbg2n.default-release" reads as "default-release".
        var separator = folderName.IndexOf('.');
        if (separator > 0)
            folderName = folderName[(separator + 1)..];

        return string.IsNullOrWhiteSpace(configuredName) ? folderName : $"{configuredName} · {folderName}";
    }

    internal static List<ProfileEntries> LoadAll(
        List<BrowserProfileConfig> profiles,
        bool indexBookmarks,
        bool indexHistory,
        IReadOnlyList<string>? blacklist = null)
    {
        var result = new List<ProfileEntries>();
        if (!indexBookmarks && !indexHistory)
            return result;

        var normalizedBlacklist = BrowserEntryFilter.NormalizeBlacklist(blacklist);

        foreach (var profile in ExpandProfiles(profiles))
        {
            try
            {
                var family = BrowserFamilyDetector.Detect(profile.Path);
                var entries = new ProfileEntries { Profile = profile, Family = family };
                if (family == BrowserFamily.Firefox)
                {
                    // Firefox keeps both in one places.sqlite, read together in a single pass -- only the
                    // disabled half is discarded here, not skipped at the read.
                    var (bookmarks, history) = FirefoxPlacesReader.Read(profile.Path);
                    if (indexBookmarks)
                        entries.Bookmarks.AddRange(bookmarks);
                    if (indexHistory)
                        entries.History.AddRange(history);
                    AttachFavicons(entries, FirefoxFaviconReader.Read(profile.Path));
                }
                else
                {
                    // Bookmarks and history are separate reads for Chromium -- skip the (often much
                    // larger, see the plugin's IndexHistory setting) history read entirely rather than
                    // reading it just to discard it.
                    if (indexBookmarks)
                        entries.Bookmarks.AddRange(ChromiumBookmarksReader.Read(profile.Path));
                    if (indexHistory)
                        entries.History.AddRange(ChromiumHistoryReader.Read(profile.Path));
                    AttachChromiumFavicons(entries, profile.Path);
                }

                entries.Bookmarks.RemoveAll(entry => BrowserEntryFilter.IsBlacklisted(entry, normalizedBlacklist));
                entries.History.RemoveAll(entry => BrowserEntryFilter.IsBlacklisted(entry, normalizedBlacklist));
                result.Add(entries);
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log($"[BrowserData] Failed to load profile '{profile.Path}': {ex.Message}", PluginSdk.LogLevel.Error);
            }
        }
        return result;
    }

    private static void AttachChromiumFavicons(ProfileEntries entries, string profileDir) => AttachFavicons(entries, ChromiumFaviconReader.Read(profileDir));

    private static void AttachFavicons(ProfileEntries entries, IReadOnlyDictionary<string, byte[]> icons)
    {
        foreach (var entry in entries.Bookmarks.Concat(entries.History))
        {
            if (icons.TryGetValue(entry.Url, out var imageData))
                entry.Favicon = BrowserFaviconIconLoader.Decode(imageData);
        }
    }
}
