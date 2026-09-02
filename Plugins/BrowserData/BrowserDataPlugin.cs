using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData;

// One configured browser profile directory to index (bookmarks + history). Path supports Windows
// environment variables (e.g. %LOCALAPPDATA%), expanded at load time in BrowserDataCache -- lets the
// schema default below point at Edge/Chrome's fixed default-profile location without baking in a specific
// username, while still reading as an ordinary, visible, user-editable setting (not something silently
// detected/injected at runtime): open Settings and it's just there, pre-filled, like any other default.
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
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "HistoryTriggerKeyword",
                LabelKey = "BrowserData_Config_HistoryTriggerKeywordLabel",
                DescriptionKey = "BrowserData_Config_HistoryTriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "bh",
                RequireNonEmpty = true
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
                Key = "Profiles",
                LabelKey = "BrowserData_Config_ProfilesLabel",
                DescriptionKey = "BrowserData_Config_ProfilesDesc",
                FieldType = ConfigFieldType.Array,
                // Chromium's profile folder name is fixed ("Default") regardless of who's logged in or
                // when it was installed, so %LOCALAPPDATA%\...\User Data\Default is a safe default to ship
                // -- unlike Firefox, whose profile folder name is randomized per-profile specifically so it
                // can't be guessed this way (not worth the added complexity of parsing profiles.ini here).
                // A user on whose machine these don't apply (no such browser, a non-default profile) just
                // sees these two rows and edits/removes them like any other default value; nothing is
                // probed or injected behind their back, and BrowserDataCache.LoadAll already skips a path
                // that doesn't exist without any special-casing needed for that here.
                DefaultValue = new List<object>
                {
                    new Dictionary<string, object> { { "Name", "Edge" }, { "Icon", "" }, { "Path", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default" } },
                    new Dictionary<string, object> { { "Name", "Chrome" }, { "Icon", "" }, { "Path", @"%LOCALAPPDATA%\Google\Chrome\User Data\Default" } }
                },
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
