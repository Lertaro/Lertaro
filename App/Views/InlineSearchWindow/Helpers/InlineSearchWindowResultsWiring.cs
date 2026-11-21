using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Connects scrolling, selection, and mouse handlers for inline result and action lists.
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
