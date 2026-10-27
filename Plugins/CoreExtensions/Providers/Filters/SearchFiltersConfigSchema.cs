using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

/// <summary>
/// Keeps the search-filter configuration schema separate from the plugin registration file.
/// </summary>
internal static class SearchFiltersConfigSchema
{
    public static PluginConfigField Create()
    {
        // The settings page reads a persisted array straight out of storage instead of through the provider, so
        // this is the one other moment where the old two-list split must not be shown for another session.
        QueryTokens.CustomFilterQueryTokenProvider.MergeSidebarRowsIntoFiltersOnce();

        return new PluginConfigField
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
                        CreateToggle(TypeFilterProvider.ApplicationFilterEnabledKey, "CoreExtensions_Config_BuiltInAppFilterLabel"),
                        CreateToggle(TypeFilterProvider.SettingsFilterEnabledKey, "CoreExtensions_Config_BuiltInSettingsFilterLabel"),
                        CreateToggle(TypeFilterProvider.DocumentFilterEnabledKey, "CoreExtensions_Config_BuiltInDocumentFilterLabel"),
                        CreateToggle(TypeFilterProvider.ImageFilterEnabledKey, "CoreExtensions_Config_BuiltInImageFilterLabel"),
                        CreateToggle(TypeFilterProvider.VideoFilterEnabledKey, "CoreExtensions_Config_BuiltInVideoFilterLabel"),
                        CreateToggle(TypeFilterProvider.AudioFilterEnabledKey, "CoreExtensions_Config_BuiltInAudioFilterLabel"),
                        CreateToggle(TypeFilterProvider.ZipFilterEnabledKey, "CoreExtensions_Config_BuiltInZipFilterLabel")
                    }
                }
            }
        };
    }

    private static PluginConfigField CreateToggle(string key, string labelKey) => new()
    {
        Key = key,
        LabelKey = labelKey,
        FieldType = ConfigFieldType.Boolean,
        DefaultValue = true
    };
}
