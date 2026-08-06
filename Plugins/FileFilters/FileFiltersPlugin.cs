using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters;

public class FileFiltersPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("FileFilters_PluginName");
    public string Description => TranslationService.Get("FileFilters_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "Filters",
                LabelKey = "FileFilters_Config_FiltersLabel",
                DescriptionKey = "FileFilters_Config_FiltersDesc",
                FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(),
                SubFields = new List<PluginConfigField>
                {
                    new PluginConfigField
                    {
                        Key = "Enabled",
                        LabelKey = "FileFilters_Config_EnabledLabel",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },
                    new PluginConfigField
                    {
                        Key = "Name",
                        LabelKey = "FileFilters_Config_NameLabel",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Keyword",
                        LabelKey = "FileFilters_Config_KeywordLabel",
                        DescriptionKey = "FileFilters_Config_KeywordDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = ""
                    },
                    new PluginConfigField
                    {
                        Key = "Folders",
                        LabelKey = "FileFilters_Config_FoldersLabel",
                        DescriptionKey = "FileFilters_Config_FoldersDesc",
                        FieldType = ConfigFieldType.StringList,
                        DefaultValue = new List<string>()
                    },
                    new PluginConfigField
                    {
                        Key = "FilterPattern",
                        LabelKey = "FileFilters_Config_FilterPatternLabel",
                        DescriptionKey = "FileFilters_Config_FilterPatternDesc",
                        FieldType = ConfigFieldType.Text,
                        DefaultValue = "*"
                    }
                }
            }
        }
    };
}
