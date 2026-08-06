using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Wires up the results/actions list's scroll, selection, and mouse-click event handlers -- split out
// of InlineSearchWindow's constructor to keep that file under the line-count limit.
internal static class InlineSearchWindowResultsWiring
{
    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        var inputHandler = window.InputHandler;
        var lstResults = window.LstResults;

        lstResults.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((s, e) => inputHandler.UpdateShortcutHints()));
        lstResults.SelectionChanged += (s, e) =>
        {
            inputHandler.SyncExplorerSelection();
            inputHandler.UpdatePathPreviewVisibility();
        };

        lstResults.PreviewMouseLeftButtonUp += (s, e) =>
        {
            var item = InlineSearchWindowInputHandler.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null && item.Content is AppSearchResult result)
            {
                e.Handled = true;
                var asAdmin = Keyboard.Modifiers == ModifierKeys.Control;
                InlineSearchNavigator.ExecuteSearchResult(window, result, asAdmin);
            }
        };

        lstResults.PreviewMouseRightButtonUp += (s, e) =>
        {
            var item = InlineSearchWindowInputHandler.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null && item.Content is AppSearchResult result)
            {
                e.Handled = true;
                lstResults.SelectedItem = result;
                window.MenuPresenter.EnterActionsMode(result);
            }
        };

        window.LstActions.PreviewMouseLeftButtonUp += window.MenuPresenter.HandleActionsPreviewMouseLeftButtonUp;
    }
}
