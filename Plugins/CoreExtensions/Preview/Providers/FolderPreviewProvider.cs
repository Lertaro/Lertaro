using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
namespace Lertaro.Plugins.CoreExtensions.Preview.Providers;
public class FolderPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("QuickLook_FolderProviderName");
    public int Priority => 10;
    public bool CanPreview(string path, bool isDir) => isDir;
    private readonly record struct FolderRowData(string Name, string FullPath, bool IsDir, ImageSource? Icon, bool NeedsIconLoad);

    public UIElement CreatePreview(string path, bool isDir)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new StackPanel { Margin = new Thickness(4) };
        scroll.Content = panel;

        // EnumerateFileSystemInfos hits the disk/network per call -- over a network drive with many
        // entries this blocked the whole window until it finished (same class of bug as the thumbnail one
        // above). Data gathering (no WPF elements -- those are thread-affine and can't be created off the
        // UI thread) happens in the background; the rows themselves are only ever built on the UI thread,
        // once, from that data. Each row's icon starts as whatever's already cached (instant, see
        // GetIconFromCacheOnly) and upgrades itself in place once the real one loads (see BuildRow) --
        // same cache-first-then-upgrade pattern AppSearchResult.Icon already uses for the results grid.
        Task.Run(() => CollectRows(path)).ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion)
            {
                panel.Children.Add(BuildMessageRow($"{TranslationService.Get("QuickLook_Error")}: {t.Exception?.GetBaseException().Message}", isError: true));
                return;
            }

            var (rows, truncatedCount) = t.Result;
            if (rows.Count == 0)
            {
                panel.Children.Add(BuildMessageRow(TranslationService.Get("QuickLook_FolderEmpty"), isError: false));
                return;
            }

            foreach (var row in rows)
                panel.Children.Add(BuildRow(row));

            if (truncatedCount > 0)
            {
                var moreItemsText = new TextBlock
                {
                    Text = TranslationService.Get("QuickLook_MoreItems"),
                    Margin = new Thickness(24, 4, 0, 0),
                    FontSize = 11,
                    FontStyle = FontStyles.Italic
                };
                moreItemsText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                panel.Children.Add(moreItemsText);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());

        return scroll;
    }

    // Runs entirely off the UI thread -- returns plain data (icons are already-frozen ImageSources, safe
    // to hand across threads) for the UI-thread continuation above to turn into rows. Icons use the
    // cache-only fast path (no disk/shell access) so a folder full of not-yet-cached items (videos
    // especially) doesn't just move the same blocking cost from "before any row appears" to "before this
    // one Task.Run resolves" -- BuildRow below kicks off the real per-item fetch afterward instead.
    private static (List<FolderRowData> Rows, int TruncatedCount) CollectRows(string path)
    {
        var dirInfo = new DirectoryInfo(path);
        var items = dirInfo.EnumerateFileSystemInfos()
            .Where(static item => ShouldDisplay(item.Attributes))
            .Take(31)
            .ToList();
        var displayCount = Math.Min(items.Count, 30);
        var rows = new List<FolderRowData>(displayCount);
        for (var idx = 0; idx < displayCount; idx++)
        {
            var item = items[idx];
            var isItemDir = (item.Attributes & FileAttributes.Directory) != 0;
            var icon = IconService.GetIconFromCacheOnly(item.FullName, isItemDir, out var needsLoad);
            rows.Add(new FolderRowData(item.Name, item.FullName, isItemDir, icon, needsLoad));
        }
        return (rows, items.Count > 30 ? items.Count - 30 : 0);
    }

    internal static bool ShouldDisplay(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    // Builds one row with whatever icon CollectRows already had cached, and -- only if that was just a
    // placeholder -- fetches the real one in the background and swaps it in once ready. No staleness guard
    // needed: img belongs only to this row's own Image control, which is either still showing (correct) or
    // long gone from the visual tree (harmless no-op) by the time the fetch resolves.
    private static UIElement BuildRow(FolderRowData row)
    {
        var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var img = new Image
        {
            Source = row.Icon,
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 0, 8, 0)
        };
        rowPanel.Children.Add(img);
        var nameText = new TextBlock
        {
            Text = row.Name,
            FontSize = 12
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        rowPanel.Children.Add(nameText);

        if (row.NeedsIconLoad)
        {
            Task.Run(() => IconService.GetIcon(row.FullPath, row.IsDir)).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                    img.Source = t.Result;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return rowPanel;
    }

    private static TextBlock BuildMessageRow(string text, bool isError)
    {
        var row = new TextBlock
        {
            Text = text,
            FontStyle = isError ? FontStyles.Normal : FontStyles.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8)
        };
        if (isError) row.Foreground = Brushes.Red;
        else row.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        return row;
    }
}
