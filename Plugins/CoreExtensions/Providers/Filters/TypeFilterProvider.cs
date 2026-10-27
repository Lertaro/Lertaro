using Lertaro.Plugins.CoreExtensions.Models;
using Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

public class TypeFilterProvider : ISidebarFilterProvider
{
    public const string PluginId = "Lertaro.Plugins.CoreExtensions";
    public const string DocumentFilterEnabledKey = "TypeFilter_DocumentEnabled";
    public const string ImageFilterEnabledKey = "TypeFilter_ImageEnabled";
    public const string VideoFilterEnabledKey = "TypeFilter_VideoEnabled";
    public const string AudioFilterEnabledKey = "TypeFilter_AudioEnabled";
    public const string ZipFilterEnabledKey = "TypeFilter_ZipEnabled";
    public const string SidebarCustomFiltersKey = "SidebarCustomFilters";

    public int SortOrder => 1;

    // No extension table of its own: the rows below ask the one table the quick window's filter tabs and the
    // user-editable :@keyword rules also read, so a file the sidebar calls a document cannot be something
    // else under the 文档 tab. These rows used to carry three shorter lists here, which is why the sidebar
    // never offered 音频 or 压缩包 at all.
    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var group = new SidebarFilterGroup
        {
            Id = "Type",
            Header = TranslationService.Get("Filter_TypeHeader"),
            AllowMultiSelect = true
        };

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Type_Folder",
            DisplayName = TranslationService.Get("Filter_TypeFolder"),
            IconData = "M2,4 A1,1 0 0,1 3,3 H7 L9,5 H18 A1,1 0 0,1 19,6 V16 A1,1 0 0,1 18,17 H3 A1,1 0 0,1 2,16 Z",
            MatchPredicate = res => !res.IsApplication && res.IsDir
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Type_File",
            DisplayName = TranslationService.Get("Filter_TypeFile"),
            IconData = "M6 2c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6H6zm7 7V3.5L18.5 9H13z",
            MatchPredicate = res => !res.IsApplication && !res.IsDir
        });

        AddTypeRow(group, "Type_Doc", "Filter_TypeDoc", DocumentFilterEnabledKey, SearchCategoryFilter.Document,
            "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z");
        AddTypeRow(group, "Type_Image", "Filter_TypeImage", ImageFilterEnabledKey, SearchCategoryFilter.Image,
            "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5.04 10.71l-3 3.83-2.07-2.49L6 18h12l-4.04-4.29z");
        AddTypeRow(group, "Type_Video", "Filter_TypeVideo", VideoFilterEnabledKey, SearchCategoryFilter.Video,
            "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z");
        // No glyph of their own yet: an empty IconData falls back to the sidebar's default icon, which is
        // better than shipping a path nobody has looked at.
        AddTypeRow(group, "Type_Audio", "Filter_TypeAudio", AudioFilterEnabledKey, SearchCategoryFilter.Audio, null);
        AddTypeRow(group, "Type_Zip", "Filter_TypeZip", ZipFilterEnabledKey, SearchCategoryFilter.Archive, null);

        AddCustomFilters(group);
        return new[] { group };
    }

    private static void AddTypeRow(SidebarFilterGroup group, string id, string labelKey, string enabledKey,
        SearchCategoryFilter category, string? iconData)
    {
        if (!IsFilterEnabled(enabledKey))
            return;

        group.Items.Add(new SidebarFilterItem
        {
            Id = id,
            DisplayName = TranslationService.Get(labelKey),
            IconData = iconData,
            // Applications stay out of the extension rows, as they always were here: a Start Menu shortcut
            // names itself an application and belongs to that row, not to whichever kind its target has.
            MatchPredicate = res => !res.IsApplication && !res.IsDir
                && SearchCategoryClassifier.Classify(res.FullPath, isDirectory: false) == category
        });
    }

    private static void AddCustomFilters(SidebarFilterGroup group)
    {
        var sidebarFilters = PluginSettingsService.GetSetting<List<CustomFilterItem>>(PluginId, SidebarCustomFiltersKey, []);
        var referencedFilters = CustomFilterQueryTokenProvider.GetConfiguredFilters();
        var prefix = CustomFilterQueryTokenProvider.GetConfiguredPrefix();
        for (var i = 0; i < sidebarFilters.Count; i++)
        {
            var filter = sidebarFilters[i];
            if (!filter.Enabled)
                continue;

            var name = filter.Keyword?.Trim();
            var rule = CustomFilterRuleResolver.Expand(filter.Rule, referencedFilters, prefix, allowDisabledReferences: true);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rule))
                continue;

            group.Items.Add(new SidebarFilterItem
            {
                Id = $"Type_Custom_{i}",
                DisplayName = name,
                IconData = string.IsNullOrWhiteSpace(filter.Icon) ? null : filter.Icon.Trim(),
                MatchPredicate = CustomFilterQueryTokenProvider.BuildPredicate(filter.Rule, referencedFilters, prefix, allowDisabledReferences: true)
            });
        }
    }

    private static bool IsFilterEnabled(string key) => PluginSettingsService.GetSetting(PluginId, key, true);
}
