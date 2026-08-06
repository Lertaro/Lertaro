using System.Text.Json;

namespace Lertaro.Core;

public class UserSettings
{
    public List<NetworkDriveSetting> NetworkDrives { get; set; } = new();
    public List<WslSetting> WslSettings { get; set; } = new();
    public List<FolderIndexSetting> FolderIndexes { get; set; } = new();
    public DefaultFileManagerSetting DefaultFileManager { get; set; } = new();
    public List<FavoriteItemSetting> Favorites { get; set; } = new();
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "%SystemDrive%\\Windows.old",
        "%ProgramData%",
        "%SystemRoot%",
        "%ProgramW6432%",
        "%USERPROFILE%\\AppData",
        "%ProgramFiles(x86)%"
    };
    public List<string> IgnoredPathGlobs { get; set; } = new()
    {
        ".*",
        "~*",
        "\\$*",
        "node_modules"
    };
    public List<string> IgnoredPathRegexes { get; set; } = new();
    public List<string> BlacklistedProcesses { get; set; } = new();
    public bool EnableHistory { get; set; } = true;
    public bool EnableKeywordHistory { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoSilentUpdate { get; set; } = false;
    // Applied only to QuickSearchWindow (Window_Loaded), not process-wide -- see GitHub issue #82
    // (NVIDIA Advanced Optimus GPU hot-switch blocked by this window's persistent DirectX composition
    // surface, since it's created once at startup and only ever hidden, never closed). Requires a
    // restart to take effect, since the window's HwndTarget.RenderMode is only set once at load.
    public bool EnableHardwareAcceleration { get; set; } = true;

    // Off makes every bare query term a contiguous-substring match instead of a subsequence one
    // (fzf's own --exact mode). Default on, so an upgrade never changes what a query matches.
    public bool EnableFuzzyMatch { get; set; } = true;
    // The Quick window's tray-menu capsule button (only shown while this is true) is the replacement
    // entry point for Settings/Exit/etc., so hiding the tray icon never strands the user -- see
    // QuickSearchWindow's BtnTrayMenu and TrayIconService.ShowMenuAt.
    public bool HideTrayIcon { get; set; } = false;
    public string GlobalTokenPrefix { get; set; } = ":";
    public string LogLevel { get; set; } = "Info";
    public string PreferredLanguage { get; set; } = GetDefaultSystemLanguage();
    public string Theme { get; set; } = "Light";
    public bool ThemeFollowSystem { get; set; } = false;
    // Empty means "unset" -- themes come entirely from plugins, so there's no safe hardcoded default
    // here; ThemeManager.ResolveLightDarkThemeId falls back to whatever theme is first available.
    public string LightThemeId { get; set; } = string.Empty;
    public string DarkThemeId { get; set; } = string.Empty;
    public HotkeyPageSettings Hotkeys { get; set; } = new();
    public SearchWindowSettings SearchWindow { get; set; } = new();
    public PreviewWindowSettings PreviewWindow { get; set; } = new();
    public MainWindowSettings MainWindow { get; set; } = new();
    public QuickPanelSettings QuickPanel { get; set; } = new();
    public LocalSendSettingsModel LocalSend { get; set; } = new();

    private static string GetDefaultSystemLanguage()
    {
        try
        {
            return System.Globalization.CultureInfo.CurrentUICulture.Name;
        }
        catch
        {
            return "en-US";
        }
    }

    /// <summary>
    /// Stores IDs of disabled plugin sub-components (actions, dynamic providers, instant providers, filter providers, column providers).
    /// Format: "{PluginDllFileName}::{ComponentType}::{ComponentName}"
    /// </summary>
    public List<string> DisabledPluginComponents { get; set; } = new();

    /// <summary>
    /// User-chosen display order for IQuickNavigationProvider entries in the quick-navigation menu's
    /// root level, most-preferred first. Same id format as DisabledPluginComponents. A provider whose
    /// id isn't present here yet (newly installed, or never reordered) falls back to its original
    /// discovery order, appended after every listed provider -- see PluginManager.QuickNavigationProviders.
    /// </summary>
    public List<string> QuickNavigationProviderOrder { get; set; } = new();

    /// <summary>
    /// User-chosen display order for the full SearchWindow's sidebar filter groups (Type/Date/Size/any
    /// third-party ISidebarFilterProvider), most-preferred first. Same id format as
    /// DisabledPluginComponents, one id per PROVIDER (not per group -- a provider contributing multiple
    /// groups moves them together as a unit). A provider whose id isn't present here yet falls back to
    /// its own SortOrder, same convention QuickNavigationProviderOrder above uses for discovery order.
    /// </summary>
    public List<string> SidebarGroupOrder { get; set; } = new();

    /// <summary>
    /// User-chosen left-to-right display order for the full SearchWindow's results grid columns
    /// (built-in "Name"/"Path"/"DateModified" plus any third-party IResultColumnProvider's own
    /// ColumnId), most-preferred first. Purely which columns show in which position -- NOT which
    /// column the rows are currently sorted by (see SearchResultSortMemory, deliberately in-memory-
    /// only and not settings-backed). A column whose id isn't present here yet falls back to its
    /// natural discovery position, same convention QuickNavigationProviderOrder above uses.
    /// </summary>
    public List<string> ColumnOrder { get; set; } = new();

    /// <summary>
    /// User-chosen priority order for the quick window's search-result "types" -- each
    /// ISearchableItemProvider (Applications, Settings, File Filters, any third-party plugin) plus one
    /// synthetic entry for raw file-index results (SearchResultTypePriority.FilesTypeId), most-preferred
    /// first. Sits as a hard tier between history/favorite priority and match-quality weight in
    /// SearchResultMapper.RankAndDedupe -- see RankedCandidate.TypeRank -- so e.g. Applications can be
    /// made to always outrank Files regardless of which matched the query text better, without any
    /// small weight bonus getting lost against a much better textual match. An id not listed here yet
    /// falls back to int.MaxValue, same convention as
    /// QuickNavigationProviderOrder above. Quick window only, same scope as the feature it replaces --
    /// the inline window's own ranking (ExplorerSearchHelper) never consults this list.
    /// </summary>
    public List<string> ResultTypeOrder { get; set; } = new();

    /// <summary>
    /// User-chosen display order for the Actions menu's own sections -- the built-in group ("__builtin__",
    /// or "static::{GroupName}" for a static action that sets a custom GroupName) plus one id per
    /// IDynamicActionProvider (e.g. the Custom Actions/自定义动作 group), most-preferred first. A section
    /// whose id isn't listed here yet falls back to its natural position: built-in first, then dynamic
    /// providers by ascending Priority -- see ActionMenuBuilder's own ordering.
    /// </summary>
    public List<string> ActionMenuGroupOrder { get; set; } = new();

    /// <summary>
    /// User-chosen priority order for IFilePreviewProvider (built-in image/text/media/PE previewers plus
    /// any third-party plugin's own), most-preferred first. Same id format as DisabledPluginComponents.
    /// A provider whose id isn't present here yet falls back to its own Priority (higher first), same
    /// convention QuickNavigationProviderOrder above uses for discovery order -- see
    /// PluginManager.FilePreviewProviders.
    /// </summary>
    public List<string> FilePreviewProviderOrder { get; set; } = new();

    /// <summary>
    /// User-chosen priority order for IThumbnailProvider (the built-in shell thumbnail provider plus
    /// any third-party plugin's own), most-preferred first. Same id format as DisabledPluginComponents.
    /// A provider whose id isn't present here yet falls back to its own Priority (higher first), same
    /// convention FilePreviewProviderOrder above uses -- see PluginManager.ThumbnailProviders.
    /// </summary>
    public List<string> ThumbnailProviderOrder { get; set; } = new();

    /// <summary>
    /// Per-type trigger character for the quick window's exclusive result-type filter -- key is the
    /// same type-id ResultTypeOrder above uses, value is a single character (an entry is only present
    /// when the user actually configured one). When the FIRST character the user types matches a
    /// configured trigger, only that type's candidates enter the ranked competition in
    /// SearchResultMapper.BuildQuickResults -- Favorites/history are unaffected, since they're
    /// hardcoded top-priority regardless of any of this. See SearchResultTypePriority.ResolveTrigger.
    /// Quick window only, same scope as ResultTypeOrder.
    /// </summary>
    public Dictionary<string, string> ResultTypeTriggers { get; set; } = new();

    public Dictionary<string, Dictionary<string, object>> PluginSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public T GetPluginSetting<T>(string pluginId, string key, T defaultValue)
    {
        if (PluginSettings.TryGetValue(pluginId, out var settingsDict) && settingsDict.TryGetValue(key, out var val))
        {
            try
            {
                if (val is T typedVal)
                {
                    return typedVal;
                }
                if (val is JsonElement element)
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText())!;
                }
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        return defaultValue;
    }

    public void SetPluginSetting(string pluginId, string key, object? value)
    {
        if (!PluginSettings.TryGetValue(pluginId, out var settingsDict))
        {
            settingsDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            PluginSettings[pluginId] = settingsDict;
        }
        if (value == null)
        {
            settingsDict.Remove(key);
        }
        else
        {
            settingsDict[key] = value;
        }

        PluginSdk.Services.PluginSettingsService.NotifySettingChanged(pluginId, key);
    }

    private static readonly Lazy<string> UserDataDirectory = new(() =>
    {
        SettingsDataDirectoryMigrator.Migrate(Logger.UserDataDir, updateUserSettings: true);
        return Logger.UserDataDir;
    });

    public static string SettingsPath => Path.Combine(UserDataDirectory.Value, "user-settings.json");

    private static UserSettings? _cachedSettings;
    private static string? _lastJsonOnDisk;
    private static readonly object _cacheLock = new();

    public static UserSettings Load()
    {
        lock (_cacheLock)
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            _cachedSettings = LoadFromDisk();
            return _cachedSettings;
        }
    }

    public static UserSettings ForceReload()
    {
        lock (_cacheLock)
        {
            _cachedSettings = LoadFromDisk();
            return _cachedSettings;
        }
    }

    private static UserSettings LoadFromDisk()
    {
        var retries = 5;
        while (retries > 0)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new UserSettings();

                using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                lock (_cacheLock)
                {
                    _lastJsonOnDisk = json;
                }
                var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                NormalizeHotkeys(settings);
                return settings;
            }
            catch (IOException)
            {
                retries--;
                if (retries <= 0) throw;
                Task.Delay(50).Wait();
            }
            catch (Exception ex)
            {
                Logger.Log($"[UserSettings] Failed to load settings: {ex.Message}", Core.LogLevel.Warn);
                return new UserSettings();
            }
        }
        return new UserSettings();
    }

    // A blank ToggleWindowHotkey leaves GlobalHotkeyDetector.CheckToggleWindowHotkey with no modifier
    // and no key to match against, so the hotkey silently stops firing altogether -- with no way back
    // into Settings other than editing the JSON file by hand. Applied on both load and save so it's
    // bulletproof regardless of how the value ended up empty (a settings file edited by hand, a
    // recorder-control edge case that clears it, or a pre-existing file from before this normalization
    // existed).
    internal static void NormalizeHotkeys(UserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Hotkeys.ToggleWindowHotkey))
            settings.Hotkeys.ToggleWindowHotkey = new HotkeyPageSettings().ToggleWindowHotkey;
    }

    public void Save()
    {
        NormalizeHotkeys(this);
        Directory.CreateDirectory(Logger.UserDataDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(this, options);

        lock (_cacheLock)
        {
            if (json == _lastJsonOnDisk)
            {
                _cachedSettings = this;
                return;
            }
        }

        RotateBackups(SettingsPath);

        var retries = 5;
        while (retries > 0)
        {
            try
            {
                using var stream = new FileStream(SettingsPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream);
                writer.Write(json);
                break;
            }
            catch (IOException)
            {
                retries--;
                if (retries <= 0) throw;
                Task.Delay(50).Wait();
            }
        }

        lock (_cacheLock)
        {
            _cachedSettings = this;
            _lastJsonOnDisk = json;
        }
        ExclusionRuleSet.InvalidateCache();
    }

    internal static void RotateBackups(string filePath, int maxBackups = 5) => UserSettingsBackupStore.Rotate(filePath, maxBackups);
}

