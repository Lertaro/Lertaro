using Lertaro.Plugins.BrowserData.Readers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData;

internal sealed class ProfileEntries
{
    public required BrowserProfileConfig Profile { get; init; }
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

    internal static bool IsComponentEnabled => PluginSettingsService.IsComponentEnabled(
        PluginDllName, ComponentType, ComponentName);

    // SqliteCopyReader's own snapshot copies are meant to live only for the duration of a single
    // ReadCopy call and delete themselves in a finally block -- but that delete is best-effort (a
    // locked file, e.g. still held by an antivirus scan, silently leaves the copy behind), and each
    // one gets a fresh GUID name, so a failed delete orphans it permanently with nothing else to ever
    // clean it up. Swept on every reload (not just once at process start) since this cache reloads
    // every RefreshInterval for as long as Lertaro keeps running -- waiting for the next app restart
    // could otherwise be days, during which orphans from failed deletes keep piling up unattended (see
    // issue: multi-MB browser History/places.sqlite copies filling up the temp drive over time).
    private static void CleanupOrphanedTempFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), "lertaro_browserdata_*"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }

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
    // "bm <query>" of the session doesn't land on a still-empty snapshot -- same reload path GetSnapshot
    // already uses, just triggered proactively instead of waiting for the first query.
    public static void Preload() => MaybeTriggerReload();

    private static void MaybeTriggerReload()
    {
        if (!IsComponentEnabled)
            return;

        var configured = PluginSettingsService.GetSetting<List<BrowserProfileConfig>>("Lertaro.Plugins.BrowserData", "Profiles", null!);
        var indexBookmarks = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", "IndexBookmarks", true);
        var indexHistory = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", "IndexHistory", true);
        // Bookmarks/history toggles folded into the same reload signature as Profiles -- flipping either
        // one should take effect on the next query, not wait for the up-to-10-minute staleness timer.
        var signature = (configured != null ? System.Text.Json.JsonSerializer.Serialize(configured) : string.Empty)
            + $"|{indexBookmarks}|{indexHistory}";

        var needsReload = signature != _lastSignature || DateTime.UtcNow - _lastLoadUtc > RefreshInterval;
        if (!needsReload)
            return;

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
                CleanupOrphanedTempFiles();
                var loaded = LoadAll(configured ?? new List<BrowserProfileConfig>(), indexBookmarks, indexHistory);
                lock (Lock)
                {
                    _snapshot = loaded;
                }
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

    internal static List<ProfileEntries> LoadAll(List<BrowserProfileConfig> profiles, bool indexBookmarks, bool indexHistory)
    {
        var result = new List<ProfileEntries>();
        if (!indexBookmarks && !indexHistory)
            return result;

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Path))
                continue;

            // %LOCALAPPDATA%-style Windows env vars, expanded here (not stored expanded) so the schema
            // default in BrowserDataPlugin.cs can point at a fixed browser install location without
            // baking in a specific username, and so the settings UI keeps showing the readable
            // "%LOCALAPPDATA%\..." form rather than one particular machine's resolved absolute path.
            var expandedPath = Environment.ExpandEnvironmentVariables(profile.Path);
            if (!Directory.Exists(expandedPath))
                continue;

            try
            {
                var family = BrowserFamilyDetector.Detect(expandedPath);
                var entries = new ProfileEntries { Profile = profile };
                switch (family)
                {
                    case BrowserFamily.Chromium:
                        // Bookmarks and history are separate reads for Chromium -- skip the (often much
                        // larger, see the plugin's IndexHistory setting) history read entirely rather than
                        // reading it just to discard it.
                        if (indexBookmarks)
                            entries.Bookmarks.AddRange(ChromiumBookmarksReader.Read(expandedPath));
                        if (indexHistory)
                            entries.History.AddRange(ChromiumHistoryReader.Read(expandedPath));
                        break;
                    case BrowserFamily.Firefox:
                        // Firefox keeps both in one places.sqlite, read together in a single pass -- only
                        // the disabled half is discarded here, not skipped at the read.
                        var (bookmarks, history) = FirefoxPlacesReader.Read(expandedPath);
                        if (indexBookmarks)
                            entries.Bookmarks.AddRange(bookmarks);
                        if (indexHistory)
                            entries.History.AddRange(history);
                        break;
                    default:
                        PluginSdk.Logger.Log($"[BrowserData] '{expandedPath}' doesn't look like a Chrome/Firefox profile folder (no Bookmarks/History/places.sqlite found), skipping.", PluginSdk.LogLevel.Warn);
                        continue;
                }
                result.Add(entries);
            }
            catch (Exception ex)
            {
                PluginSdk.Logger.Log($"[BrowserData] Failed to load profile '{expandedPath}': {ex.Message}", PluginSdk.LogLevel.Error);
            }
        }
        return result;
    }
}
