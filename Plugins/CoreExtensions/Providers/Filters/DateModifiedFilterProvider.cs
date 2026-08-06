using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Filters;

public class DateModifiedFilterProvider : ISidebarFilterProvider
{
    public int SortOrder => 2;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var group = new SidebarFilterGroup
        {
            Header = TranslationService.Get("Filter_DateHeader")
            // AllowMultiSelect deliberately left false: these ranges are cumulative (Date_7 already
            // includes everything Date_1 does), not disjoint buckets, so OR-ing several would just
            // collapse to the widest one selected -- single-select (with the group's own clear button
            // to get back to "no filter") is the meaningful interaction here.
        };

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_1",
            DisplayName = TranslationService.Get("Filter_Date1"),
            IconData = "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
            FilterPredicate = results => Task.FromResult(FilterByDate(results, DateTime.Now.AddDays(-1)))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_7",
            DisplayName = TranslationService.Get("Filter_Date7"),
            IconData = "M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V10h14v10zm0-12H5V6h14v2z",
            FilterPredicate = results => Task.FromResult(FilterByDate(results, DateTime.Now.AddDays(-7)))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_30",
            DisplayName = TranslationService.Get("Filter_Date30"),
            IconData = "M19 4h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V10h14v10zm0-12H5V6h14v2z",
            FilterPredicate = results => Task.FromResult(FilterByDate(results, DateTime.Now.AddDays(-30)))
        });

        group.Items.Add(new SidebarFilterItem
        {
            Id = "Date_365",
            DisplayName = TranslationService.Get("Filter_Date365"),
            IconData = "M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-9 14H7v-7h3v7zm4 0h-3V7h3v10zm4 0h-3v-4h3v4z",
            FilterPredicate = results => Task.FromResult(FilterByDate(results, DateTime.Now.AddDays(-365)))
        });

        return new[] { group };
    }

    // Synchronous: Modified is already known from the index via ISearchResult.Metadata for every real
    // file result, so this no longer needs FileMetadataService's IPC round trip at all. A result whose
    // metadata isn't available (Metadata.Modified == DateTime.MinValue) is excluded, same as it
    // silently was before when its path had no entry in the batch lookup's response.
    private static IReadOnlyList<ISearchResult> FilterByDate(IReadOnlyList<ISearchResult> results, DateTime cutoff) =>
        results.Where(r => r.Metadata.Modified != DateTime.MinValue && r.Metadata.Modified >= cutoff).ToList();
}
