using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core;

namespace Lertaro.App;

// Derives display values for an AppSearchResult using the row's lazy display state.
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
        if (extras.Icon != null && !extras.IconNeedsRefresh)
            return extras.Icon;

        var previousIcon = extras.Icon;
        extras.Icon = ShellIconHelper.GetIconFromCacheOnly(owner.FullPath, owner.IsDir, out var needsLoad);
        if (needsLoad && previousIcon != null) extras.Icon = previousIcon;
        extras.IconNeedsRefresh = false;
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

        _ = GetDateModifiedAsync(owner);
        return DateTime.MinValue;
    }

    internal static Task<DateTime> GetDateModifiedAsync(AppSearchResult owner)
    {
        var known = owner.Metadata.Modified;
        if (known != DateTime.MinValue) return Task.FromResult(known);
        if (WslPath.IsPath(owner.FullPath)) return Task.FromResult(DateTime.MinValue);
        var extras = owner.DisplayExtras;
        lock (extras)
        {
            if (extras.DateModified is { } cached) return Task.FromResult(cached);
            return extras.DateModifiedTask ??= LoadDateModifiedAsync(owner);
        }
    }

    public static bool[]? GetHighlightMask(AppSearchResult owner, string text, string query) =>
        owner.SourceProvider is PluginSdk.Abstractions.Plugins.IInstantResultProvider provider
            ? PluginPerformanceMonitor.Measure(provider, () => provider.GetHighlightMask(text, query))
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
            if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) && !ViewModels.Search.SearchReachabilityGate.IsPathReachable(path))
                return Task.CompletedTask;

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
        },
        // IconLoadingStarted is one-way and GetIcon short-circuits on a non-null Icon, so a row whose permit
        // never came free would keep its placeholder for the rest of the session. Clearing both here lets the
        // next binding refresh queue the load again -- the quick window re-notifies every persisted row when it
        // is shown or rescaled, which is what makes an aborted load self-healing instead of permanent.
        onWaitTimedOut: () => LazyBackgroundLoader.ApplyOnUiThread(() =>
        {
            owner.DisplayExtras.Icon = null;
            owner.DisplayExtras.IconLoadingStarted = false;
        }));
    }

    private static Task<DateTime> LoadDateModifiedAsync(AppSearchResult owner)
    {
        var path = owner.FullPath;
        var isDirectory = owner.IsDir;
        return Task.Run(async () =>
        {
            if (!await DateModifiedSemaphore.WaitAsync(5000).ConfigureAwait(false))
            {
                var extras = owner.DisplayExtras;
                lock (extras) extras.DateModifiedTask = null;
                return DateTime.MinValue;
            }

            var modified = DateTime.MinValue;
            try
            {
                if ((!path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)
                    || ViewModels.Search.SearchReachabilityGate.IsPathReachable(path))
                    && (isDirectory ? Directory.Exists(path) : File.Exists(path)))
                    modified = isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);
            }
            catch
            {
                modified = DateTime.MinValue;
            }
            finally { DateModifiedSemaphore.Release(); }

            LazyBackgroundLoader.ApplyOnUiThread(() =>
            {
                owner.DisplayExtras.DateModified = modified;
                owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.DateModified));
                owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.DateModifiedText));
            });
            return modified;
        });
    }
}
