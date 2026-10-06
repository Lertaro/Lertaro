using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Providers;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch;

/// <summary>
/// Entry point for the ContentSearch plugin, managing configuration schemas, lifecycle, and index scheduling.
/// </summary>
public sealed class ContentSearchPlugin : IPlugin, IConfigurable
{
    private const string PluginId = "Lertaro.Plugins.ContentSearch";
    private static readonly string PluginDllName = Path.GetFileName(typeof(ContentSearchPlugin).Assembly.Location);
    private static readonly object RuntimeLock = new();

    public static ContentSearchDatabase? Database { get; private set; }
    public static ContentIndexScheduler? Scheduler { get; private set; }

    static ContentSearchPlugin()
    {
        PluginSettingsService.ComponentEnablementChanged += UpdateRuntimeState;
        PluginSettingsService.SettingChanged += (id, _) =>
        {
            if (string.Equals(id, PluginId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateRuntimeState();
            }
        };
        UpdateRuntimeState();
    }

    private static void UpdateRuntimeState()
    {
        lock (RuntimeLock)
        {
            ContentIndexScheduler? startingScheduler = null;
            try
            {
                if (!ContentSearchEnablement.IsRuntimeEnabled(
                    PluginSettingsService.IsComponentEnabled, PluginDllName))
                {
                    // Keep ready storage for in-flight queries and later re-enablement.
                    Scheduler?.Dispose();
                    Scheduler = null;
                    return;
                }

                if (Database == null)
                {
                    var baseDir = UserDataService.GetUserDataDirectory();
                    var dataFolder = !string.IsNullOrEmpty(baseDir)
                        ? Path.Combine(baseDir, "ContentIndex")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lertaro", "ContentIndex");
                    var database = new ContentSearchDatabase(Path.Combine(dataFolder, "content_index.db"));
                    database.Initialize();
                    Database = database;
                }

                var config = LoadConfigFromSettings();
                if (Scheduler == null)
                {
                    startingScheduler = new ContentIndexScheduler(Database);
                    startingScheduler.ProgressChanged += () =>
                        SearchRefreshService.RefreshIfMatches(ContentSearchInstantProvider.IsPlaceholderQuery);
                    startingScheduler.Start(config);
                    Scheduler = startingScheduler;
                }
                else
                {
                    Scheduler.UpdateConfig(config);
                    Scheduler.TriggerFullScan();
                }
            }
            catch (Exception ex)
            {
                // A failed type initializer permanently poisons the plugin, including its settings UI.
                // Leave failed startup unpublished so a settings/enablement change can retry it.
                PluginSdk.Logger.Log($"[ContentSearch] Runtime initialization or update failed: {ex}", PluginSdk.LogLevel.Error);
                try { startingScheduler?.Dispose(); }
                catch (Exception cleanupError)
                {
                    PluginSdk.Logger.Log($"[ContentSearch] Failed to clean up scheduler: {cleanupError}", PluginSdk.LogLevel.Error);
                }
            }
        }
    }

    public string Name => TranslationService.Get("ContentSearch_PluginName");
    public string Description => TranslationService.Get("ContentSearch_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields = new List<PluginConfigField>
        {
            new()
            {
                Key = "TriggerKeyword",
                IsTriggerWord = true,
                LabelKey = "ContentSearch_Config_TriggerLabel",
                DescriptionKey = "ContentSearch_Config_TriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "cs",
                RequireNonEmpty = true
            },
            new()
            {
                Key = "MonitoredFolders",
                LabelKey = "ContentSearch_Config_FoldersLabel",
                DescriptionKey = "ContentSearch_Config_FoldersDesc",
                FieldType = ConfigFieldType.StringList,
                DefaultValue = new List<string>()
            },
            new()
            {
                Key = "MaxFileSizeMb",
                LabelKey = "ContentSearch_Config_MaxSizeLabel",
                DescriptionKey = "ContentSearch_Config_MaxSizeDesc",
                FieldType = ConfigFieldType.Integer,
                DefaultValue = 10
            },
            new()
            {
                Key = "MaxIndexSizeMb",
                LabelKey = "ContentSearch_Config_IndexSizeLabel",
                DescriptionKey = "ContentSearch_Config_IndexSizeDesc",
                FieldType = ConfigFieldType.Integer,
                DefaultValue = 5120
            },
            new()
            {
                Key = "IndexedExtensions",
                LabelKey = "ContentSearch_Config_ExtensionsLabel",
                DescriptionKey = "ContentSearch_Config_ExtensionsDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "txt,md,pdf,docx,docm,pptx,pptm,xlsx,xlsm,csv"
            },
            new()
            {
                Key = "ExcludedNamePatterns",
                LabelKey = "ContentSearch_Config_ExclusionsLabel",
                DescriptionKey = "ContentSearch_Config_ExclusionsDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = string.Empty
            },
            new()
            {
                Key = "ClearIndex",
                LabelKey = "ContentSearch_Config_ClearLabel",
                DescriptionKey = "ContentSearch_Config_ClearDesc",
                FieldType = ConfigFieldType.Button,
                DefaultValue = string.Empty,
                // Clear-only: unlike RebuildIndex, this does not trigger a full scan.
                OnClick = () => Task.Run(() => Database?.ClearAll())
            },
            new()
            {
                Key = "RebuildIndex",
                LabelKey = "ContentSearch_Config_RebuildLabel",
                DescriptionKey = "ContentSearch_Config_RebuildDesc",
                FieldType = ConfigFieldType.Button,
                DefaultValue = string.Empty,
                // Off the UI thread: ClearAll runs DELETE + VACUUM, seconds on a large index.
                OnClick = () => Task.Run(() =>
                {
                    lock (RuntimeLock)
                    {
                        if (Scheduler is { } scheduler)
                            scheduler.RebuildIndex();
                        else if (Database is { } database)
                        {
                            database.ClearAll();
                            database.InitialIndexCompleted = false;
                        }
                    }
                })
            }
        },
        OnSave = UpdateRuntimeState
    };

    private static ContentIndexConfig LoadConfigFromSettings()
    {
        var rawFolders = PluginSettingsService.GetSetting(
            PluginId,
            "MonitoredFolders",
            new List<string>());

        var maxSizeMb = PluginSettingsService.GetSetting(PluginId, "MaxFileSizeMb", 0);
        var maxIndexSizeMb = PluginSettingsService.GetSetting(PluginId, "MaxIndexSizeMb", 5120);
        var extsStr = PluginSettingsService.GetSetting(PluginId, "IndexedExtensions", string.Empty);
        var exclusionsStr = PluginSettingsService.GetSetting(PluginId, "ExcludedNamePatterns", string.Empty);

        var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(extsStr))
        {
            foreach (var rawExt in extsStr.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var ext = rawExt.Trim();
                if (!ext.StartsWith('.'))
                    ext = "." + ext;
                extSet.Add(ext);
            }
        }

        return new ContentIndexConfig
        {
            MonitoredFolders = rawFolders ?? new List<string>(),
            MaxFileSizeBytes = maxSizeMb > 0 ? maxSizeMb * 1024L * 1024L : long.MaxValue,
            MaxIndexSizeBytes = maxIndexSizeMb > 0 ? maxIndexSizeMb * 1024L * 1024L : long.MaxValue,
            AllowedExtensions = extSet,
            ExcludedPatterns = ContentIndexConfig.ParseExcludedPatterns(exclusionsStr)
        };
    }
}
