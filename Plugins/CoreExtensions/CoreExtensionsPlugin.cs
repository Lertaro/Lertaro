using Lertaro.Plugins.CoreExtensions.Actions;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;
namespace Lertaro.Plugins.CoreExtensions;

public class CoreExtensionsPlugin : IPlugin, IActionProvider, IConfigurable
{
    public string Name => TranslationService.Get("Plugins_CoreActionPluginName");
    public string Description => TranslationService.Get("CoreExtensions_PluginDesc");

    public IEnumerable<ISearchResultAction> GetActions() => new ISearchResultAction[]
        {
            new OpenResultAction(),
            new OpenResultAsAdminAction(),
            new LocateInExplorerAction(),
            new CopyNameAction(),
            new CopyPathAction(),
            new AddFavoriteAction(),
            new LocalSendAction(),
            new CutFileAction(),
            new CopyFileAction(),
            new PasteFileAction(),
            new DeleteFileAction(),
            new PermanentDeleteFileAction(),
            new OpenCommandPromptAction(),
            new OpenAdminCommandPromptAction(),
            new TouchAction(),
            new MkdirAction()
        };

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => new IDynamicActionProvider[]
        {
            new ShellMenuActionProvider()
        };

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "ThumbnailGroup",
                LabelKey = "CoreExtensions_Config_ThumbnailGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "ThumbnailExtensions",
                        LabelKey = "CoreExtensions_Config_ThumbnailExtensionsLabel",
                        DescriptionKey = "CoreExtensions_Config_ThumbnailExtensionsDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = new List<string>
                        {
                            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
                            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".m4v"
                        }
                    }
                }
            },
            new PluginConfigField
            {
                Key = "CustomFoldersGroup",
                LabelKey = "CoreExtensions_Config_CustomFoldersGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "CustomFolders",
                        LabelKey = "CoreExtensions_Config_CustomFoldersLabel",
                        DescriptionKey = "CoreExtensions_Config_CustomFoldersDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = new List<string>()
                    }
                }
            },
            // The quick panel's Recent Files tab, whose settings belong to whoever provides that tab
            // rather than to the panel: the panel knows about folders and tabs, not about what any one
            // tab needs to be told. Defaults matter here -- they are what the tab shows before anybody
            // has been to this page, and RecentFilesTabProvider reads them through the same schema.
            new PluginConfigField
            {
                Key = "RecentFilesGroup",
                LabelKey = "CoreExtensions_Config_RecentFilesGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = Providers.QuickPanel.RecentFilesTabProvider.DirectoriesKey,
                        LabelKey = "CoreExtensions_Config_RecentFilesDirectoriesLabel",
                        DescriptionKey = "CoreExtensions_Config_RecentFilesDirectoriesDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = Providers.QuickPanel.RecentFilesTabProvider.DefaultDirectories()
                    },
                    new PluginConfigField
                    {
                        Key = Providers.QuickPanel.RecentFilesTabProvider.CountKey,
                        LabelKey = "CoreExtensions_Config_RecentFilesCountLabel",
                        FieldType = ConfigFieldType.Integer,
                        DefaultValue = 10
                    },
                    new PluginConfigField
                    {
                        Key = Providers.QuickPanel.RecentFilesTabProvider.MaxAgeKey,
                        LabelKey = "CoreExtensions_Config_RecentFilesMaxAgeLabel",
                        DescriptionKey = "CoreExtensions_Config_RecentFilesMaxAgeDesc",
                        FieldType = ConfigFieldType.Integer,
                        DefaultValue = 60
                    }
                }
            },
            new PluginConfigField
            {
                Key = "SearchSettingsTrigger",
                LabelKey = "CoreExtensions_Config_SearchSettingsTriggerLabel",
                DescriptionKey = "CoreExtensions_Config_SearchSettingsTriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "set"
            },
            new PluginConfigField
            {
                Key = "InlineSearchGroup",
                LabelKey = "CoreExtensions_Config_InlineSearchGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "InlineSearchAlwaysOpen",
                        LabelKey = "CoreExtensions_Config_InlineSearchAlwaysOpenLabel",
                        DescriptionKey = "CoreExtensions_Config_InlineSearchAlwaysOpenDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    }
                }
            },
            Providers.Filters.SearchFiltersConfigSchema.Create(),
            new PluginConfigField
            {
                Key = "QueryTokensGroup",
                LabelKey = "CoreExtensions_Config_QueryTokensGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = Providers.QueryTokens.PathExclusionQueryTokenProvider.SettingKey,
                        LabelKey = "CoreExtensions_Config_PathExclusionPrefixLabel",
                        DescriptionKey = "CoreExtensions_Config_PathExclusionPrefixDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ":",
                        MaxLength = 1,
                        RequireNonEmpty = true
                    },
                    new PluginConfigField
                    {
                        Key = Providers.QueryTokens.CustomFilterQueryTokenProvider.PrefixSettingKey,
                        LabelKey = "CoreExtensions_Config_CustomFilterPrefixLabel",
                        DescriptionKey = "CoreExtensions_Config_CustomFilterPrefixDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = "@",
                        MaxLength = 1,
                        RequireNonEmpty = true
                    },
                    new PluginConfigField
                    {
                        Key = Providers.QueryTokens.WildcardQueryTokenProvider.PrefixSettingKey,
                        LabelKey = "CoreExtensions_Config_WildcardFilterPrefixLabel",
                        DescriptionKey = "CoreExtensions_Config_WildcardFilterPrefixDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = "?",
                        MaxLength = 1,
                        RequireNonEmpty = true
                    }
                }
            },
            new PluginConfigField
            {
                Key = "CustomFiltersGroup",
                LabelKey = "CoreExtensions_Config_CustomFiltersGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = Providers.QueryTokens.CustomFilterQueryTokenProvider.SettingKey,
                        LabelKey = "CoreExtensions_Config_CustomFiltersLabel",
                        DescriptionKey = "CoreExtensions_Config_CustomFiltersDesc",
                        FieldType = ConfigFieldType.Array,
                        DefaultValue = Providers.QueryTokens.CustomFilterQueryTokenProvider.DefaultFiltersSchema(),
                        SubFields = new List<PluginConfigField>
                        {
                            new PluginConfigField
                            {
                                Key = "Enabled",
                                LabelKey = "CoreExtensions_Config_CustomFilters_EnabledLabel",
                                FieldType = ConfigFieldType.Boolean,
                                DefaultValue = true
                            },
                            new PluginConfigField
                            {
                                Key = "Keyword",
                                LabelKey = "CoreExtensions_Config_CustomFilters_KeywordLabel",
                                DescriptionKey = "CoreExtensions_Config_CustomFilters_KeywordDesc",
                                FieldType = ConfigFieldType.Text,
                                DefaultValue = ""
                            },
                            new PluginConfigField
                            {
                                Key = "Rule",
                                LabelKey = "CoreExtensions_Config_CustomFilters_RuleLabel",
                                DescriptionKey = "CoreExtensions_Config_CustomFilters_RuleDesc",
                                FieldType = ConfigFieldType.Text,
                                DefaultValue = ""
                            }
                        }
                    }
                }
            }
        }
    };
}
