using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FolderCascader;

public class FolderCascaderPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("FolderCascader_PluginName");
    public string Description => TranslationService.Get("FolderCascader_PluginDesc");



    public class FolderConfigItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string SubMenu { get; set; } = string.Empty;
    }

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "ContentGroup",
                LabelKey = "FolderCascader_Group_Content",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "ShowFavorites",
                        LabelKey = "FolderCascader_Config_ShowFavorites",
                        DescriptionKey = "FolderCascader_Config_ShowFavoritesDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "ShowHistory",
                        LabelKey = "FolderCascader_Config_ShowHistory",
                        DescriptionKey = "FolderCascader_Config_ShowHistoryDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },

                    new PluginConfigField
                    {
                        Key = "Folders",
                        LabelKey = "FolderCascader_Config_FoldersLabel",
                        DescriptionKey = "FolderCascader_Config_FoldersDesc",
                        FieldType = ConfigFieldType.Array,
                        DefaultValue = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}" }
                            },
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{20d04fe0-3aea-1069-a2d8-08002b30309d}" }
                            },
                            new Dictionary<string, object>
                            {
                                { "Name", "" },
                                { "Path", "shell:::{450d8fba-ad25-11d0-98a8-0800361b1103}" }
                            }
                        },
                        SubFields = new List<PluginConfigField>
                        {
                            new PluginConfigField
                            {
                                Key = "Name",
                                LabelKey = "FolderCascader_Config_FolderName",
                                FieldType = ConfigFieldType.Text,
                                DefaultValue = ""
                            },
                            new PluginConfigField
                            {
                                Key = "Path",
                                LabelKey = "FolderCascader_Config_FolderPath",
                                FieldType = ConfigFieldType.FolderPath,
                                DefaultValue = ""
                            },
                            new PluginConfigField
                            {
                                Key = "SubMenu",
                                LabelKey = "FolderCascader_Config_SubMenuLabel",
                                DescriptionKey = "FolderCascader_Config_SubMenuDesc",
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
