using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

public class FileSizeFilterProvider : ISidebarFilterProvider
{
    public int SortOrder => 3;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var group = new SidebarFilterGroup
        {
            Header = TranslationService.Get("Filter_SizeHeader"),
            AllowMultiSelect = true
        };

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Small",
            DisplayName = TranslationService.Get("Filter_SizeSmall"),
            IconData = "M12 13.5c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z",
            FilterPredicate = results => Task.FromResult(FilterBySize(results, size => size < 1 * 1024 * 1024))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Medium",
            DisplayName = TranslationService.Get("Filter_SizeMedium"),
            IconData = "M12 14.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z",
            FilterPredicate = results => Task.FromResult(FilterBySize(results, size => size >= 1 * 1024 * 1024 && size <= 100 * 1024 * 1024))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Size_Large",
            DisplayName = TranslationService.Get("Filter_SizeHuge"),
            IconData = "M12 16.5c-2.48 0-4.5-2.02-4.5-4.5s2.02-4.5 4.5-4.5 4.5 2.02 4.5 4.5-2.02 4.5-4.5 4.5z",
            FilterPredicate = results => Task.FromResult(FilterBySize(results, size => size > 100 * 1024 * 1024))
        });

        return new[] { group };
    }

    // Synchronous: Size is already known from the index via ISearchResult.Metadata for every real
    // file result, so this no longer needs FileMetadataService's IPC round trip at all. A result whose
    // metadata isn't available (Metadata.Modified == DateTime.MinValue -- not backed by the file index,
    // e.g. a plugin-provided item) is excluded, same as it silently was before when its path had no
    // entry in the batch lookup's response.
    private static IReadOnlyList<ISearchResult> FilterBySize(IReadOnlyList<ISearchResult> results, Func<long, bool> matches) =>
        results.Where(r => !r.IsDir && r.Metadata.Modified != DateTime.MinValue && matches(r.Metadata.Size)).ToList();
}
