using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CustomCommands;

public class CustomCommandsPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("CustomCommands_PluginName");
    public string Description => TranslationService.Get("CustomCommands_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "Commands",
                LabelKey = "CustomCommands_Config_CommandsLabel",
                DescriptionKey = "CustomCommands_Config_CommandsDesc",
                FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(),
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "Enabled",
                        LabelKey = "CustomCommands_Config_EnabledLabel",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },
                    new PluginConfigField
                    {
                        Key = "Keyword",
                        LabelKey = "CustomCommands_Config_KeywordLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Title",
                        LabelKey = "CustomCommands_Config_TitleLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Path",
                        LabelKey = "CustomCommands_Config_PathLabel",
                        FieldType = ConfigFieldType.FilePath,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Parameter",
                        LabelKey = "CustomCommands_Config_ParameterLabel",
                        DescriptionKey = "CustomCommands_Config_ParameterDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Icon",
                        LabelKey = "CustomCommands_Config_IconLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "WorkingDir",
                        LabelKey = "CustomCommands_Config_WorkingDirLabel",
                        FieldType = ConfigFieldType.FolderPath,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "RunSilently",
                        LabelKey = "CustomCommands_Config_RunSilentlyLabel",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = false
                    },
                    new PluginConfigField
                    {
                        Key = "RunAsAdmin",
                        LabelKey = "CustomCommands_Config_RunAsAdminLabel",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = false
                    },
                    new PluginConfigField
                    {
                        Key = "ShowInQuickNav",
                        LabelKey = "CustomCommands_Config_ShowInQuickNavLabel",
                        DescriptionKey = "CustomCommands_Config_ShowInQuickNavDesc",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = false
                    },
                    new PluginConfigField
                    {
                        Key = "SubMenu",
                        LabelKey = "CustomCommands_Config_SubMenuLabel",
                        DescriptionKey = "CustomCommands_Config_SubMenuDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    }
                }
            }
        }
    };
}
