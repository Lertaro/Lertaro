using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.App.Services.AppWindow;

namespace Lertaro.App.Services.ShellMenu.Presenter;

/// <summary>
/// Handles mouse input events for the actions list in shell menu mode.
/// Extracted from ShellMenuPresenter to keep it under 300 lines.
/// </summary>
internal sealed class ShellMenuMouseInputHandler
{
    private readonly ShellMenuPresenter _presenter;
    private readonly ISearchWindow _view;
    private System.Windows.Point? _lastHoverPos;

    public ShellMenuMouseInputHandler(ShellMenuPresenter presenter, ISearchWindow view)
    {
        _presenter = presenter;
        _view = view;
    }

    // Mirrors LstResults' own MouseMove wiring in ResultsControl.xaml.cs (hovering a row selects it) --
    // the actions list never had this, so it was the odd one out: the results list picks up a hover
    // as selection, but right-clicking into the actions menu lost that behavior entirely. Same
    // synthetic-MouseMove guard as the results list: WPF re-hit-tests a stationary cursor whenever the
    // list's rows relayout underneath it (e.g. after ApplyFilter reloads items), which would otherwise
    // steal selection away from the filter's own first-selectable default.
    public void HandleActionsMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(_view.LstActions);
        if (_lastHoverPos.HasValue && pos == _lastHoverPos.Value) return;
        _lastHoverPos = pos;

        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Content is ActionMenuItem actionItem
            && !actionItem.IsSeparator && !actionItem.IsSectionHeader && !actionItem.IsDisabled
            && !ReferenceEquals(_view.LstActions.SelectedItem, actionItem))
        {
            _view.LstActions.SelectedItem = actionItem;
        }
    }

    public void ReseedHoverBaseline() => _lastHoverPos = Mouse.GetPosition(_view.LstActions);

    public void HandleActionsPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _presenter.GoBackMenuOrExit();
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
