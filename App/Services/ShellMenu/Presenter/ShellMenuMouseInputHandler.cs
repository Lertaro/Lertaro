using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Views.Controls.Results;

namespace Lertaro.App.Services.ShellMenu.Presenter;

/// <summary>
/// Handles mouse input events for the actions list in shell menu mode.
/// Extracted from ShellMenuPresenter to keep it under 300 lines.
/// </summary>
internal sealed class ShellMenuMouseInputHandler
{
    private readonly ShellMenuPresenter _presenter;
    private readonly ISearchWindow _view;
    private System.Windows.Point? _lastScreenPosition;

    public ShellMenuMouseInputHandler(ShellMenuPresenter presenter, ISearchWindow view)
    {
        _presenter = presenter;
        _view = view;
    }

    // WPF re-hit-tests a stationary cursor when the dynamic menu relayouts. List-relative coordinates
    // can change as the window resizes/moves, so use the same screen-position guard as result rows
    // to avoid stealing the keyboard selection without physical pointer movement.
    public void HandleActionsMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!ResultsHoverSelection.TryGetScreenPosition(out var position)
            || !ResultsHoverSelection.UpdatePointerPosition(ref _lastScreenPosition, position))
            return;

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is ActionMenuItem actionItem
            && !actionItem.IsSeparator && !actionItem.IsSectionHeader && !actionItem.IsDisabled
            && !ReferenceEquals(_view.LstActions.SelectedItem, actionItem))
        {
            _view.LstActions.SelectedItem = actionItem;
        }
    }

    public void ReseedHoverBaseline() => _lastScreenPosition =
        ResultsHoverSelection.TryGetScreenPosition(out var position) ? position : null;

    public void HandleActionsPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SearchInputHelper.HandleActionsEscape(_view, _presenter);
    }

    public void HandleActionsPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is ActionMenuItem actionItem)
        {
            if (actionItem.IsSeparator || actionItem.IsSectionHeader || actionItem.IsDisabled)
            {
                e.Handled = true;
                return;
            }

            if (actionItem.HasSubMenu)
            {
                e.Handled = true;
                _view.LstActions.SelectedItem = actionItem;
                _presenter.EnterSubMenu();
            }

            else
            {
                e.Handled = true;
                _view.LstActions.SelectedItem = actionItem;
                _presenter.ExecuteSelectedAction();
            }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            if (child is FrameworkContentElement fce)
                child = fce.Parent;
            else
                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
