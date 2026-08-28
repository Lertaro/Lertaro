using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch;

/// <summary>
/// Entry point for the ContentSearch plugin, managing configuration schemas, lifecycle, and index scheduling.
/// </summary>
public sealed class ContentSearchPlugin : IPlugin, IConfigurable
{
    private const string PluginId = "Lertaro.Plugins.ContentSearch";

    public static ContentSearchDatabase Database { get; }
    public static ContentIndexScheduler Scheduler { get; }

    static ContentSearchPlugin()
    {
        var baseDir = UserDataService.GetUserDataDirectory();
        var dataFolder = !string.IsNullOrEmpty(baseDir)
            ? Path.Combine(baseDir, "ContentIndex")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lertaro", "ContentIndex");

        var dbPath = Path.Combine(dataFolder, "content_index.db");
        Database = new ContentSearchDatabase(dbPath);
        Database.Initialize();

        Scheduler = new ContentIndexScheduler(Database);
        Scheduler.Start(LoadConfigFromSettings());

        PluginSettingsService.SettingChanged += (id, _) =>
        {
            if (string.Equals(id, PluginId, StringComparison.OrdinalIgnoreCase))
            {
                Scheduler.UpdateConfig(LoadConfigFromSettings());
            }
        };
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
                LabelKey = "ContentSearch_Config_TriggerLabel",
                DescriptionKey = "ContentSearch_Config_TriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "c",
                RequireNonEmpty = true
            },
            new()
            {
                Key = "MonitoredFolders",
                LabelKey = "ContentSearch_Config_FoldersLabel",
                DescriptionKey = "ContentSearch_Config_FoldersDesc",
                FieldType = ConfigFieldType.StringList,
                DefaultValue = new List<string>
                {
                    @"%USERPROFILE%\Documents",
                    @"%USERPROFILE%\Desktop"
                }
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
                Key = "IndexedExtensions",
                LabelKey = "ContentSearch_Config_ExtensionsLabel",
                DescriptionKey = "ContentSearch_Config_ExtensionsDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "txt,md,cs,json,xml,csv,docx,pdf,py,js,ts,html,css,log,yaml,yml,sql"
            },
            new()
            {
                Key = "EnableSemanticSearch",
                LabelKey = "ContentSearch_Config_EnableSemanticLabel",
                DescriptionKey = "ContentSearch_Config_EnableSemanticDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = false
            }
        },
        OnSave = () =>
        {
            Scheduler.UpdateConfig(LoadConfigFromSettings());
            Scheduler.TriggerFullScan();
        }
    };

    private static ContentIndexConfig LoadConfigFromSettings()
    {
        var rawFolders = PluginSettingsService.GetSetting(
            PluginId,
            "MonitoredFolders",
            new List<string> { @"%USERPROFILE%\Documents", @"%USERPROFILE%\Desktop" });

        var maxSizeMb = PluginSettingsService.GetSetting(PluginId, "MaxFileSizeMb", 10);
        var extsStr = PluginSettingsService.GetSetting(PluginId, "IndexedExtensions", "txt,md,cs,json,xml,csv,docx,pdf,py,js,ts,html,css,log,yaml,yml,sql");
        var enableSemantic = PluginSettingsService.GetSetting(PluginId, "EnableSemanticSearch", false);

        var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawExt in extsStr.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = rawExt.Trim();
            if (!ext.StartsWith('.'))
                ext = "." + ext;
            extSet.Add(ext);
        }

        return new ContentIndexConfig
        {
            MonitoredFolders = rawFolders ?? new List<string>(),
            MaxFileSizeBytes = Math.Max(1, maxSizeMb) * 1024L * 1024L,
            AllowedExtensions = extSet,
            EnableSemanticSearch = enableSemantic
        };
    }
}
