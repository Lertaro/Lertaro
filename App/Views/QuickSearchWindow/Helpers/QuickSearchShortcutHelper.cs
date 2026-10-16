using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Computes and assigns the Ctrl+N-style shortcut-hint labels shown on each visible result row.
// Mirrors InlineSearchWindow's own InlineSearchShortcutHelper: kept separate from
// QuickSearchWindowLayoutManager, which owns panel-height layout math -- sizing a panel and labeling a
// row's keyboard shortcut are unrelated concerns that only happen to run back-to-back after a resize.
internal static class QuickSearchShortcutHelper
{
    public static void UpdateShortcutHints(Lertaro.App.QuickSearchWindow window, ScrollViewer? scrollViewer)
    {
        // GetFirstVisibleIndex copes with both scrolling modes; this list is item-based throughout now
        // (QuickSearchWindowLayoutManager sets CanContentScroll to true), so the offset is already an item
        // index and the row-height argument is only used for the pixel-based case.
        var firstVisible = WpfUiHelper.GetFirstVisibleIndex(scrollViewer, UiMetrics.ScaledNormalRowHeight);
        var shortcutIndex = 1;

        var selectMod = UserSettings.Load().Hotkeys.SelectJumpModifier;

        for (var i = 0; i < window.LstResults.Items.Count; i++)
        {
            if (window.LstResults.Items[i] is AppSearchResult item)
            {
                if (item.IsEmptyResult || item.IsSearchSectionHeader || string.IsNullOrEmpty(selectMod))
                {
                    item.ShortcutHint = string.Empty;
                    item.ShortcutVisibility = Visibility.Collapsed;
                    continue;
                }

                if (i >= firstVisible && shortcutIndex <= 9)
                {
                    var prefix = string.Equals(selectMod, "None", StringComparison.OrdinalIgnoreCase) ? "" : $"{selectMod}+";
                    item.ShortcutHint = $"{prefix}{shortcutIndex}";
                    item.ShortcutVisibility = Visibility.Visible;
                    shortcutIndex++;
                }
                else
                {
                    item.ShortcutHint = string.Empty;
                    item.ShortcutVisibility = Visibility.Collapsed;
                }
            }
        }
    }
}
