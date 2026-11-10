using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData;

// One configured browser entry to index (bookmarks + history). Path supports Windows environment variables
// (e.g. %LOCALAPPDATA%) and may name either a single profile folder or a folder that holds several, which
// BrowserDataCache expands at load time -- see Readers.BrowserProfileDirectories. That's what lets the
// schema default below point at a browser's fixed data location without baking in a specific username or
// a random per-install profile name, while still reading as an ordinary, visible, user-editable setting
// (not something silently detected/injected at runtime): open Settings and it's just there, pre-filled,
// like any other default.
public class BrowserProfileConfig
{
    public string Name { get; set; } = string.Empty;
    // Key/property named "Icon" specifically (not "IconData") -- the settings UI's icon-preview swatch
    // (Templates.xaml's IsIconField trigger) only activates for a Text field whose key is exactly "Icon".
    public string Icon { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class BrowserDataPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("BrowserData_PluginName");
    public string Description => TranslationService.Get("BrowserData_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "BookmarkTriggerKeyword",
                LabelKey = "BrowserData_Config_BookmarkTriggerKeywordLabel",
                DescriptionKey = "BrowserData_Config_BookmarkTriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "bb",
                RequireNonEmpty = true,
                Validation = ConfigFieldValidation.TriggerKeyword
            },
            new PluginConfigField
            {
                Key = "HistoryTriggerKeyword",
                LabelKey = "BrowserData_Config_HistoryTriggerKeywordLabel",
                DescriptionKey = "BrowserData_Config_HistoryTriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "bh",
                RequireNonEmpty = true,
                Validation = ConfigFieldValidation.TriggerKeyword
            },
            new PluginConfigField
            {
                Key = "IndexBookmarks",
                LabelKey = "BrowserData_Config_IndexBookmarksLabel",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = "IndexHistory",
                LabelKey = "BrowserData_Config_IndexHistoryLabel",
                DescriptionKey = "BrowserData_Config_IndexHistoryDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = "Blacklist",
                LabelKey = "BrowserData_Config_BlacklistLabel",
                DescriptionKey = "BrowserData_Config_BlacklistDesc",
                FieldType = ConfigFieldType.StringList,
                DefaultValue = new List<string>()
            },
            new PluginConfigField
            {
                Key = "Profiles",
                LabelKey = "BrowserData_Config_ProfilesLabel",
                DescriptionKey = "BrowserData_Config_ProfilesDesc",
                FieldType = ConfigFieldType.Array,
                // Built from BrowserDataDefaults rather than written out again here: the plugin's own
                // runtime read of this setting can't see a schema default, so a second copy of the list
                // is one more thing that can quietly disagree with what actually gets indexed.
                DefaultValue = BrowserDataDefaults.ProfileSchemaDefaults(),
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "Name",
                        LabelKey = "BrowserData_Config_NameLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Icon",
                        LabelKey = "BrowserData_Config_IconDataLabel",
                        DescriptionKey = "BrowserData_Config_IconDataDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Path",
                        LabelKey = "BrowserData_Config_PathLabel",
                        DescriptionKey = "BrowserData_Config_PathDesc",
                        FieldType = ConfigFieldType.FolderPath,
                        DefaultValue = ""
                    }
                }
            }
        }
    };
}
