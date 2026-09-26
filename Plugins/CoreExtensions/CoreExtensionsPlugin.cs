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
            new RenameAction(),
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
                        // Windows' own "all apps" folder, so the field starts out naming the one app source
                        // that is meaningful without the user inventing a path. Read from the roots helper
                        // rather than written out again: it is also what the provider falls back to when the
                        // saved list is empty, and the two must not drift.
                        DefaultValue = new List<string>(Providers.Indexing.StartMenuAppFolderRoots.DefaultCustomFolders)
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
                DefaultValue = "set",
                RequireNonEmpty = true,
                Validation = ConfigFieldValidation.TriggerKeyword
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
                    },
                    // Inline-search only: those windows are the one place a plugin action is inserted ahead
                    // of the file results AND takes the default selection, so Enter runs the command instead
                    // of opening the file the user matched. The quick and full windows keep their actions
                    // either way -- which is why this belongs in this plugin's inline section rather than in
                    // the app-wide general settings.
                    new PluginConfigField
                    {
                        Key = "InlineSearchEnableSearchActions",
                        LabelKey = "CoreExtensions_Config_InlineSearchSearchActionsLabel",
                        DescriptionKey = "CoreExtensions_Config_InlineSearchSearchActionsDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    }
                }
            },
            Providers.Filters.SearchFiltersConfigSchema.Create(),
            // No prefix field here on purpose. The character that starts a plugin token belongs to the
            // search syntax -- the host's scanner lifts a word out of the query by ITS character and hands
            // the token over with that character still on the front -- so this plugin reads it from
            // SearchSyntaxService instead of keeping a copy. The copy that used to live here could only
            // agree or break, and breaking was silent: the host stripped the word, nobody claimed it, and
            // the search came back empty.
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
                                // Says "this value is a plugin query token keyword", which is what lets the
                                // host's settings page show the user the exact word to type ("\audio")
                                // instead of leaving them to assemble it from a prefix they cannot see.
                                Validation = ConfigFieldValidation.TokenKeyword,
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
