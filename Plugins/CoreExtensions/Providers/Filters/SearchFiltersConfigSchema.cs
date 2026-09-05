using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

/// <summary>
/// Keeps the search-filter configuration schema separate from the plugin registration file.
/// </summary>
internal static class SearchFiltersConfigSchema
{
    public static PluginConfigField Create() => new()
    {
        Key = "SearchFiltersGroup",
        LabelKey = "CoreExtensions_Config_SearchFiltersGroupLabel",
        FieldType = ConfigFieldType.Group,
        SubFields = new List<PluginConfigField>
        {
            new()
            {
                Key = "BuiltInTypeFiltersGroup",
                LabelKey = "CoreExtensions_Config_BuiltInTypeFiltersGroupLabel",
                FieldType = ConfigFieldType.Group,
                SubFields = new List<PluginConfigField>
                {
                    CreateToggle(TypeFilterProvider.DocumentFilterEnabledKey, "CoreExtensions_Config_BuiltInDocumentFilterLabel"),
                    CreateToggle(TypeFilterProvider.ImageFilterEnabledKey, "CoreExtensions_Config_BuiltInImageFilterLabel"),
                    CreateToggle(TypeFilterProvider.VideoFilterEnabledKey, "CoreExtensions_Config_BuiltInVideoFilterLabel")
                }
            },
            new()
            {
                Key = TypeFilterProvider.SidebarCustomFiltersKey,
                LabelKey = "CoreExtensions_Config_SidebarCustomFiltersLabel",
                DescriptionKey = "CoreExtensions_Config_SidebarCustomFiltersDesc",
                FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(),
                SubFields = new List<PluginConfigField>
                {
                    new()
                    {
                        Key = "Enabled",
                        LabelKey = "CoreExtensions_Config_CustomFilters_EnabledLabel",
                        FieldType = ConfigFieldType.Boolean,
                        DefaultValue = true
                    },
                    CreateTextField("Keyword", "CoreExtensions_Config_SidebarCustomFilters_NameLabel", "CoreExtensions_Config_SidebarCustomFilters_NameDesc"),
                    CreateTextField("Icon", "CoreExtensions_Config_SidebarCustomFilters_IconLabel", "CoreExtensions_Config_SidebarCustomFilters_IconDesc"),
                    CreateTextField("Rule", "CoreExtensions_Config_SidebarCustomFilters_RuleLabel", "CoreExtensions_Config_SidebarCustomFilters_RuleDesc")
                }
            }
        }
    };

    private static PluginConfigField CreateToggle(string key, string labelKey) => new()
    {
        Key = key,
        LabelKey = labelKey,
        FieldType = ConfigFieldType.Boolean,
        DefaultValue = true
    };

    private static PluginConfigField CreateTextField(string key, string labelKey, string descriptionKey) => new()
    {
        Key = key,
        LabelKey = labelKey,
        DescriptionKey = descriptionKey,
        FieldType = ConfigFieldType.Text,
        DefaultValue = ""
    };
}
