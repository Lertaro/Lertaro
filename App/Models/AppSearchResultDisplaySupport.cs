using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core;

namespace Lertaro.App;

// Split out purely to keep AppSearchResult under the repository's per-file line limit; this helper
// has no state of its own and operates on the result row that owns the lazy display state.
internal static class AppSearchResultDisplaySupport
{
    // Twenty at a time, not four: a visible list is a couple of dozen rows, and the exact icon of every
    // row is what the user waits for after the placeholders are already on screen. At four the list was
    // resolved in ten serial waves; one wave covers it. Shell icon lookup serialises internally, so this
    // is about queueing less, not about going wider than the shell will actually serve.
    // Twenty at a time: a visible list is a couple of dozen rows, and the exact icon of every row is what
    // the user waits for after the placeholders are already on screen. Shell icon lookup serialises
    // internally, so this is about queueing less, not about going wider than the shell will serve.
    private static readonly SemaphoreSlim IconSemaphore = new(20);

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

        // This runs on the UI thread as a binding getter, and a cache miss resolves the icon
        // SYNCHRONOUSLY through the shell. Only a miss is recorded: a hit is a dictionary lookup, and
        // recording every row call would make the probe the cost it is measuring.
        var __iconAt = Lertaro.Core.SearchIndex.PerfTrace.Begin();
        extras.Icon = ShellIconHelper.GetIconFromCacheOnly(owner.FullPath, owner.IsDir, out var needsLoad);
        // Only the misses are interesting: a hit is a dictionary lookup.
        if (needsLoad)
            Lertaro.Core.SearchIndex.PerfTrace.Mark("icon.sync", __iconAt, 0, owner.IsDir ? "dir" : "file");
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
        LazyBackgroundLoader.Start(IconSemaphore, async () =>
        {
            // No artificial delay: the list paints the placeholder icons itself before this ever runs, so a
            // wait here only added to how long the exact icon took to appear, which is the latency the user
            // actually notices. The concurrency cap keeps it from flooding the UI thread with property
            // changes.
            if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) && !ViewModels.Search.SearchReachabilityGate.IsPathReachable(path))
                return;

            var icon = ShellIconHelper.GetIconForPath(path, isDirectory);
            if (icon != null)
                // Queued, not applied one by one. Each icon re-notifies its row on the UI thread, and
                // a result list resolves dozens of them: applied individually that is dozens of row
                // repaints stacked onto the frame the user is waiting for. The queue collapses them into
                // one pass per dispatcher turn.
                QueueIcon(owner, icon);
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

    private static void LoadDateModifiedAsync(AppSearchResult owner)
    {
        var path = owner.FullPath;
        var isDirectory = owner.IsDir;
        LazyBackgroundLoader.Start(DateModifiedSemaphore, () =>
        {
            if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) && !ViewModels.Search.SearchReachabilityGate.IsPathReachable(path))
                return Task.CompletedTask;

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

    // Icons resolved in the background land here instead of being applied one at a time. Each application
    // re-notifies its row on the UI thread, and a result list resolves dozens at once -- applied individually
    // that is dozens of repaints queued behind the frame the user is waiting on. One flush per dispatcher
    // turn collapses them.
    private static readonly List<(AppSearchResult Owner, System.Windows.Media.ImageSource Icon)> PendingIcons = new();
    private static bool _iconFlushScheduled;

    private static void QueueIcon(AppSearchResult owner, System.Windows.Media.ImageSource icon)
    {
        lock (PendingIcons)
        {
            PendingIcons.Add((owner, icon));
            if (_iconFlushScheduled)
                return;
            _iconFlushScheduled = true;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            FlushIcons();
            return;
        }

        // Background, so the icon pass runs after the frame that is already queued rather than inside it.
        dispatcher.BeginInvoke(new Action(FlushIcons), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void FlushIcons()
    {
        (AppSearchResult Owner, System.Windows.Media.ImageSource Icon)[] batch;
        lock (PendingIcons)
        {
            batch = PendingIcons.ToArray();
            PendingIcons.Clear();
            _iconFlushScheduled = false;
        }

        foreach (var (owner, icon) in batch)
        {
            try
            {
                owner.DisplayExtras.Icon = icon;
                owner.NotifyDisplayPropertyChanged(nameof(AppSearchResult.Icon));
            }
            catch
            {
            }
        }
    }
}
