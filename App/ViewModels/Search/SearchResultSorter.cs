using Lertaro.PluginSdk.Abstractions;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Search;

internal static class SearchResultSorter
{
    // currentSortColumn is a stable id -- "Name"/"Path"/"DateModified" for the built-in columns (see
    // Helpers/Visuals/ColumnIdentity), or a plugin's own ResultColumnDefinition.ColumnId -- never the
    // column's displayed (translated) header text, which would break on a language switch or collide
    // between two plugins that happen to share a display string.
    public static IEnumerable<AppSearchResult> Sort(IEnumerable<AppSearchResult> resultsList, string currentSortColumn, bool isSortAscending, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(currentSortColumn))
            return resultsList;
        var textComparer = WithCancellation(StringComparer.CurrentCultureIgnoreCase, token);

        if (currentSortColumn == "Name")
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.Name, textComparer)
                : resultsList.OrderByDescending(r => r.Name, textComparer);
        }
        if (currentSortColumn == "Path")
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.FullPath, textComparer)
                : resultsList.OrderByDescending(r => r.FullPath, textComparer);
        }
        if (currentSortColumn == "DateModified")
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r.DateModified, WithCancellation(Comparer<DateTime>.Default, token))
                : resultsList.OrderByDescending(r => r.DateModified, WithCancellation(Comparer<DateTime>.Default, token));
        }

        Func<ISearchResult, ISearchResult, int>? customComparer = null;
        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            var col = provider.GetColumns().FirstOrDefault(c => c.ColumnId.Equals(currentSortColumn, StringComparison.OrdinalIgnoreCase));
            if (col != null && col.SortComparer != null)
            {
                customComparer = col.SortComparer;
                break;
            }
        }

        if (customComparer != null)
        {
            return isSortAscending
                ? resultsList.OrderBy(r => r, WithCancellation(new CustomSearchResultComparer(customComparer), token))
                : resultsList.OrderByDescending(r => r, WithCancellation(new CustomSearchResultComparer(customComparer), token));
        }

        return isSortAscending
            ? resultsList.OrderBy(r => r[currentSortColumn], textComparer)
            : resultsList.OrderByDescending(r => r[currentSortColumn], textComparer);
    }

    private static IComparer<T> WithCancellation<T>(IComparer<T> comparer, CancellationToken token) =>
        !token.CanBeCanceled ? comparer : Comparer<T>.Create((left, right) =>
        {
            token.ThrowIfCancellationRequested();
            return comparer.Compare(left, right);
        });
}

internal class CustomSearchResultComparer : IComparer<AppSearchResult>
{
    private readonly Func<ISearchResult, ISearchResult, int> _comparer;
    public CustomSearchResultComparer(Func<ISearchResult, ISearchResult, int> comparer) => _comparer = comparer;
    public int Compare(AppSearchResult? x, AppSearchResult? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return _comparer(x, y);
    }
}
