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
    public const string ApplicationFilterEnabledKey = "TypeFilter_ApplicationEnabled";
    public const string SettingsFilterEnabledKey = "TypeFilter_SettingsEnabled";

    public int SortOrder => 1;

    // No extension table of its own: the rows below ask the one table the quick window's filter tabs and the
    // user-editable :@keyword rules also read, so a file the sidebar calls a document cannot be something
    // else under the 文档 tab. These rows used to carry three shorter lists here, which is why the sidebar
    // never offered 音频 or 压缩包 at all.
    //
    // The row set, its order and its labels are also what the quick window's filter bar shows: that bar reads
    // these rows through ISidebarFilterProvider rather than keeping a list of its own, so turning a row off
    // here, renaming a :@keyword, or adding one changes the bar in the same step.
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
            Category = SearchCategoryFilter.Folder,
            MatchPredicate = res => !res.IsApplication && res.IsDir
        });

        AddTypeRow(group, "Type_App", "Filter_TypeApp", ApplicationFilterEnabledKey, SearchCategoryFilter.Application,
            "M3.5 3.5h4v4h-4z M10 3.5h4v4h-4z M16.5 3.5h4v4h-4z M3.5 10h4v4h-4z M10 10h4v4h-4z M16.5 10h4v4h-4z M3.5 16.5h4v4h-4z M10 16.5h4v4h-4z M16.5 16.5h4v4h-4z");
        // The gear the 设置 instant answers already paint, so one concept wears one shape.
        AddTypeRow(group, "Type_Settings", "Filter_TypeSettings", SettingsFilterEnabledKey, SearchCategoryFilter.Settings,
            "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z");

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
        // The note the audio preview placeholder already paints.
        AddTypeRow(group, "Type_Audio", "Filter_TypeAudio", AudioFilterEnabledKey, SearchCategoryFilter.Audio,
            "M9,3 L9,15.55 A3.5,3.5 0 1 0 11,18.7 L11,7 L18,5 L18,13.55 A3.5,3.5 0 1 0 20,16.7 L20,1 Z");
        // F0 = even-odd: without it the slot inside the box winds the same way and renders filled.
        AddTypeRow(group, "Type_Zip", "Filter_TypeZip", ZipFilterEnabledKey, SearchCategoryFilter.Archive,
            "F0 M4 4h16v4H4z M6 9h12v11H6z M9.5 12.5h5v2h-5z");

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
            Category = category,
            // One question, one answer: the row and the quick window's tab both ask SearchCategoryClassifier,
            // so a file cannot be a document here and something else there -- including a shortcut, which is
            // filed by what it points at rather than by the shell being able to launch it.
            MatchPredicate = res => SearchCategoryClassifier.Categorize(res) == category
        });
    }

    private static void AddCustomFilters(SidebarFilterGroup group)
    {
        // The same entries the :@keyword reads, narrowed to the ones flagged as rows: one list, two triggers.
        var customRows = CustomFilterQueryTokenProvider.GetConfiguredRows();
        var referencedFilters = CustomFilterQueryTokenProvider.GetConfiguredFilters();
        var prefix = CustomFilterQueryTokenProvider.GetConfiguredPrefix();
        for (var i = 0; i < customRows.Count; i++)
        {
            var filter = customRows[i];
            if (!filter.Enabled)
                continue;

            var name = filter.DisplayName;
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
