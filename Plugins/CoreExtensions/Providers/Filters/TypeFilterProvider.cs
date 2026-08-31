using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

public class TypeFilterProvider : ISidebarFilterProvider
{
    public int SortOrder => 1;

    private static readonly HashSet<string> DocExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".md", ".csv", ".ini", ".conf", ".log"
    };

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".ico", ".webp"
    };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm"
    };

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

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Type_Doc",
            DisplayName = TranslationService.Get("Filter_TypeDoc"),
            IconData = "M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z",
            MatchPredicate = res => !res.IsApplication && !res.IsDir && DocExts.Contains(Path.GetExtension(res.FullPath))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Type_Image",
            DisplayName = TranslationService.Get("Filter_TypeImage"),
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-5.04 10.71l-3 3.83-2.07-2.49L6 18h12l-4.04-4.29z",
            MatchPredicate = res => !res.IsApplication && !res.IsDir && ImageExts.Contains(Path.GetExtension(res.FullPath))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Type_Video",
            DisplayName = TranslationService.Get("Filter_TypeVideo"),
            IconData = "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z",
            MatchPredicate = res => !res.IsApplication && !res.IsDir && VideoExts.Contains(Path.GetExtension(res.FullPath))
        });

        return new[] { group };
    }
}
