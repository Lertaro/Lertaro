using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.Core;
using WpfListView = System.Windows.Controls.ListView;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Keeps launch-panel shortcut labeling and lookup together so scrolling cannot make the badge and
// the key handler disagree about which item a shortcut addresses.
internal sealed class QuickSearchLaunchShortcutSupport
{
    internal const int MaxShortcutCount = 36;
    private const double LaunchItemSlotHeight = 104;
    private readonly WpfListView _itemsListView;
    private readonly Func<IReadOnlyList<AppSearchResult>?> _itemsProvider;
    private readonly Func<int> _columnsProvider;
    private readonly Func<double> _scaleProvider;

    internal QuickSearchLaunchShortcutSupport(
        WpfListView itemsListView,
        Func<IReadOnlyList<AppSearchResult>?> itemsProvider,
        Func<int> columnsProvider,
        Func<double> scaleProvider)
    {
        _itemsListView = itemsListView;
        _itemsProvider = itemsProvider;
        _columnsProvider = columnsProvider;
        _scaleProvider = scaleProvider;
        _itemsListView.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnItemsScrollChanged));
        _itemsListView.PreviewMouseWheel += OnItemsPreviewMouseWheel;
        _itemsListView.ItemContainerGenerator.ItemsChanged += (_, _) =>
            _itemsListView.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateShortcutHints);
        _itemsListView.Loaded += (_, _) =>
            _itemsListView.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateShortcutHints);
    }

    internal AppSearchResult? GetItem(Key key)
    {
        var shortcutIndex = GetShortcutIndex(key);
        var items = _itemsProvider();
        if (shortcutIndex < 0 || items == null)
            return null;

        var firstVisible = GetFirstVisibleItemIndex(
            WpfUiHelper.GetScrollViewer(_itemsListView), _columnsProvider(), GetItemSlotHeight());
        var itemIndex = firstVisible + shortcutIndex;
        return itemIndex >= 0 && itemIndex < items.Count ? items[itemIndex] : null;
    }

    internal void UpdateShortcutHints()
    {
        var items = _itemsProvider();
        if (items == null)
            return;

        var settings = UserSettings.Load();
        var modifier = settings.Hotkeys.SelectJumpModifier;
        var showBadges = settings.QuickLaunch.ShowShortcutBadges;
        var firstVisible = GetFirstVisibleItemIndex(
            WpfUiHelper.GetScrollViewer(_itemsListView), _columnsProvider(), GetItemSlotHeight());
        for (var i = 0; i < items.Count; i++)
        {
            var shortcutIndex = i - firstVisible;
            if (!showBadges
                || string.IsNullOrEmpty(modifier)
                || shortcutIndex < 0
                || shortcutIndex >= MaxShortcutCount)
            {
                ClearHint(items[i]);
                continue;
            }

            items[i].ShortcutHint = FormatShortcutHint(shortcutIndex, modifier);
            items[i].ShortcutVisibility = System.Windows.Visibility.Visible;
        }
    }

    internal static int GetShortcutIndex(Key key)
    {
        if (key is >= Key.D1 and <= Key.D9)
            return key - Key.D1;
        if (key is >= Key.NumPad1 and <= Key.NumPad9)
            return key - Key.NumPad1;
        if (key is Key.D0 or Key.NumPad0)
            return 9;
        if (key is >= Key.A and <= Key.Z)
            return 10 + (key - Key.A);
        return -1;
    }

    internal static string FormatShortcutHint(int shortcutIndex, string modifier)
    {
        if (shortcutIndex < 0 || shortcutIndex >= MaxShortcutCount)
            return string.Empty;

        var keyText = shortcutIndex switch
        {
            <= 8 => (shortcutIndex + 1).ToString(),
            9 => "0",
            _ => ((char)('A' + shortcutIndex - 10)).ToString(),
        };
        var prefix = string.Equals(modifier, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"{modifier}+";
        return prefix + keyText;
    }

    internal static int GetFirstVisibleItemIndex(ScrollViewer? scrollViewer, int columns)
    {
        if (scrollViewer == null)
            return 0;

        var safeColumns = Math.Max(1, columns);
        var firstVisibleRow = (int)Math.Round(Math.Max(0, scrollViewer.VerticalOffset) / LaunchItemSlotHeight);
        return firstVisibleRow * safeColumns;
    }

    internal static double SnapOffsetToRow(double offset, double scrollableHeight)
    {
        if (scrollableHeight <= 0)
            return 0;

        var row = (int)Math.Round(Math.Max(0, offset) / LaunchItemSlotHeight);
        return Math.Min(row * LaunchItemSlotHeight, scrollableHeight);
    }

    private void OnItemsPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 || WpfUiHelper.GetScrollViewer(_itemsListView) is not { } scrollViewer
            || scrollViewer.ScrollableHeight <= 0)
            return;

        var itemSlotHeight = GetItemSlotHeight();
        var currentRow = (int)Math.Round(scrollViewer.VerticalOffset / itemSlotHeight);
        var targetRow = Math.Max(0, currentRow + (e.Delta < 0 ? 1 : -1));
        var targetOffset = SnapOffsetToRow(targetRow * itemSlotHeight, scrollViewer.ScrollableHeight, itemSlotHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private void OnItemsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = WpfUiHelper.GetScrollViewer(_itemsListView);
        if (scrollViewer != null && e.VerticalChange != 0)
        {
            var snappedOffset = SnapOffsetToRow(
                scrollViewer.VerticalOffset, scrollViewer.ScrollableHeight, GetItemSlotHeight());
            if (Math.Abs(snappedOffset - scrollViewer.VerticalOffset) > 0.5)
                scrollViewer.ScrollToVerticalOffset(snappedOffset);
        }

        UpdateShortcutHints();
    }

    private double GetItemSlotHeight() => LaunchItemSlotHeight * Math.Max(0.1, _scaleProvider());

    private static int GetFirstVisibleItemIndex(ScrollViewer? scrollViewer, int columns, double itemSlotHeight)
    {
        if (scrollViewer == null)
            return 0;

        var safeColumns = Math.Max(1, columns);
        var firstVisibleRow = (int)Math.Round(Math.Max(0, scrollViewer.VerticalOffset) / itemSlotHeight);
        return firstVisibleRow * safeColumns;
    }

    private static double SnapOffsetToRow(double offset, double scrollableHeight, double itemSlotHeight)
    {
        if (scrollableHeight <= 0)
            return 0;

        var row = (int)Math.Round(Math.Max(0, offset) / itemSlotHeight);
        return Math.Min(row * itemSlotHeight, scrollableHeight);
    }

    private static void ClearHint(AppSearchResult item)
    {
        item.ShortcutHint = string.Empty;
        item.ShortcutVisibility = System.Windows.Visibility.Collapsed;
    }
}
