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
        // LstResults toggles between item-based (virtualized -- VerticalOffset is already an item index)
        // and pixel-based (VerticalOffset needs converting) scrolling depending on whether the current
        // layout pass needs to clip a partial row -- see QuickSearchWindowLayoutManager.ApplyResultsLayout.
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
