using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core;

namespace Lertaro.App;

// Split out purely to keep AppSearchResult under the repository's per-file line limit; this helper
// has no state of its own and operates on the result row that owns the lazy display state.
internal static class AppSearchResultDisplaySupport
{
    private static readonly SemaphoreSlim IconSemaphore = new(4);
    private static readonly SemaphoreSlim DateModifiedSemaphore = new(8);

    public static string GetIconData(AppSearchResult owner) => owner.FullPath == "__SHOW_MORE__"
        ? "M14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z"
        : owner.IsDir
            ? "M10 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"
            : "M14 2H6c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V8l-6-6zm4 18H6V4h7v5h5v11z";

    public static System.Windows.Media.ImageSource? GetIcon(AppSearchResult owner)
    {
        if (owner.IsEmptyResult || owner.IsListItem)
            return null;
        if (owner.ExistingExtras?.IconOverride is { } iconOverride)
            return iconOverride;

        var extras = owner.DisplayExtras;
        if (extras.Icon != null)
            return extras.Icon;

        extras.Icon = ShellIconHelper.GetIconFromCacheOnly(owner.FullPath, owner.IsDir, out var needsLoad);
        if (needsLoad && !extras.IconLoadingStarted)
        {
            extras.IconLoadingStarted = true;
            LoadIconAsync(owner);
        }
        return extras.Icon;
    }

    public static DateTime GetDateModified(AppSearchResult owner)
    {
        if (WslPath.IsPath(owner.FullPath))
            return DateTime.MinValue;

        var extras = owner.DisplayExtras;
        if (!extras.DateModifiedLoadingStarted)
        {
            extras.DateModifiedLoadingStarted = true;
            LoadDateModifiedAsync(owner);
        }
        return DateTime.MinValue;
    }

    public static bool[]? GetHighlightMask(AppSearchResult owner, string text, string query) =>
        owner.SourceProvider is PluginSdk.Abstractions.Plugins.IInstantResultProvider provider
            ? provider.GetHighlightMask(text, query)
            : null;

    public static string GetColumnValue(AppSearchResult owner, string columnId)
    {
        if (string.IsNullOrEmpty(columnId))
            return string.Empty;
        if (owner.ExistingExtras?.ExtendedValues?.TryGetValue(columnId, out var cached) == true)
            return cached;

        foreach (var provider in PluginManager.Instance.ResultColumnProviders)
        {
            if (!provider.GetColumns().Any(column => column.ColumnId.Equals(columnId, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                var value = provider.GetCellValue(owner, columnId);
                (owner.DisplayExtras.ExtendedValues ??= new(StringComparer.OrdinalIgnoreCase))[columnId] = value;
                return value;
            }
            catch
            {
                return string.Empty;
            }
        }
        return string.Empty;
    }

    public static void SetColumnValue(AppSearchResult owner, string columnId, string value)
    {
        (owner.DisplayExtras.ExtendedValues ??= new(StringComparer.OrdinalIgnoreCase))[columnId] = value;
        owner.NotifyDisplayPropertyChanged("Item[]");
    }

    private static void LoadIconAsync(AppSearchResult owner)
    {
        var path = owner.FullPath;
        var isDirectory = owner.IsDir;
        LazyBackgroundLoader.Start(IconSemaphore, () =>
        {
            var icon = ShellIconHelper.GetIconForPath(path, isDirectory);
            if (icon != null)
            {
                LazyBackgroundLoader.ApplyOnUiThread(() =>
                {
                    owner.DisplayExtras.Icon = icon;
                    owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.Icon));
                });
            }
            return Task.CompletedTask;
        });
    }

    private static void LoadDateModifiedAsync(AppSearchResult owner)
    {
        var path = owner.FullPath;
        var isDirectory = owner.IsDir;
        LazyBackgroundLoader.Start(DateModifiedSemaphore, () =>
        {
            var modified = DateTime.MinValue;
            try
            {
                if (isDirectory ? Directory.Exists(path) : File.Exists(path))
                    modified = isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
            }
            catch
            {
                modified = DateTime.MinValue;
            }

            LazyBackgroundLoader.ApplyOnUiThread(() =>
            {
                owner.DisplayExtras.DateModified = modified;
                owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.DateModified));
                owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.DateModifiedText));
            });
            return Task.CompletedTask;
        });
    }
}
