using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Selector = System.Windows.Controls.Primitives.Selector;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Drag-to-reorder for an ItemsControl bound to an ObservableCollection (or any IList) -- WPF has no
/// built-in support for this. Operates purely through the non-generic IList interface every
/// ObservableCollection&lt;T&gt; implements, so one attached behavior covers every reorderable settings
/// list in the app (Favorites, Result Type Priority, Quick Navigation, Startup Panel tabs, sidebar filter
/// groups, results columns, ...) regardless of each one's own item type. Coexists with an existing
/// MoveUp/MoveDown button pair in the same item template -- this doesn't replace them (keyboard/
/// accessibility users still need a non-drag way to reorder), it just adds a mouse-drag shortcut, only
/// startable from a dedicated grip icon (see IsHandle) rather than anywhere on the row.
/// </summary>
public static class DragReorder
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(ItemsControl control, bool value) => control.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(ItemsControl control) => (bool)control.GetValue(IsEnabledProperty);

    // Marks the one small element within each item template (a grip icon, typically at the row's left
    // edge) that's allowed to start a drag -- without this, a mouse-down anywhere else on the row (its
    // label text, its background) would also pick it up, which reads as accidental/surprising rather
    // than deliberate. Only a Button/TextBox press is excluded automatically (see IsWithinHandle);
    // everything else needs to opt in explicitly via this property.
    public static readonly DependencyProperty IsHandleProperty = DependencyProperty.RegisterAttached(
        "IsHandle", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static void SetIsHandle(FrameworkElement element, bool value) => element.SetValue(IsHandleProperty, value);
    public static bool GetIsHandle(FrameworkElement element) => (bool)element.GetValue(IsHandleProperty);

    // Set on a list whose items run left-to-right (a tab strip) rather than top-to-bottom. Only the drop
    // indicator actually cares: which container the pointer is over, and what index it maps to, are the
    // same question either way. Left off, a horizontal strip drew its "it lands here" line across the
    // rows instead of between the tabs, promising a position it never meant.
    public static readonly DependencyProperty IsHorizontalProperty = DependencyProperty.RegisterAttached(
        "IsHorizontal", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static void SetIsHorizontal(ItemsControl control, bool value) => control.SetValue(IsHorizontalProperty, value);
    public static bool GetIsHorizontal(ItemsControl control) => (bool)control.GetValue(IsHorizontalProperty);

    // Keyed per-ItemsControl (not a single shared field) so two reorderable lists open in the same
    // window at once (e.g. this settings page's own sidebar-order and column-order cards) never
    // interfere with each other's in-progress drag.
    private static readonly Dictionary<ItemsControl, (Point start, bool onHandle, object? item)> _state = new();
    private static readonly Dictionary<ItemsControl, (AdornerLayer layer, DragAdorner adorner, DropIndicatorAdorner indicator, FrameworkElement container)> _drag = new();

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl control || e.NewValue is not true) return;

        control.AllowDrop = true;
        control.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        control.PreviewMouseMove += OnPreviewMouseMove;
        control.DragOver += OnDragOver;
        control.Drop += OnDrop;

        // A list that goes away mid-drag takes the drag with it. Both matter for a window that is hidden
        // rather than closed: DoDragDrop's modal loop can be unwound by something other than a drop, and
        // whatever it was painting would otherwise still be on that window's adorner layer the next time
        // it is shown.
        control.Unloaded += (_, _) => EndDrag(control);
        control.IsVisibleChanged += (_, visible) =>
        {
            if (visible.NewValue is false) EndDrag(control);
        };
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var control = (ItemsControl)sender;
        var onHandle = IsWithinHandle(e.OriginalSource as DependencyObject, control);
        _state[control] = (e.GetPosition(control), onHandle, null);
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var control = (ItemsControl)sender;
        if (!_state.TryGetValue(control, out var s) || !s.onHandle) return;

        var pos = e.GetPosition(control);
        if (Math.Abs(pos.X - s.start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - s.start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var container = FindContainer(e.OriginalSource as DependencyObject, control);
        if (container == null) return;

        var item = control.ItemContainerGenerator.ItemFromContainer(container);
        if (item == null) return;

        _state[control] = (s.start, s.onHandle, item);

        // Anything a previous drag left behind goes first. These adorners sit on the window's own layer,
        // and the windows this runs in are hidden and reused rather than closed, so one that outlived
        // its drag stays painted across every reopen -- a ghost row, showing whatever its source looked
        // like when it was orphaned rather than what that row says now.
        ClearAdorners(control);

        // Renders a floating, drop-shadowed snapshot of the whole row that follows the cursor (updated
        // in OnDragOver below) so the drag actually reads as "picking the row up," not just a bare
        // cursor change -- the original row dims in place to mark where it's being lifted from.
        var layer = AdornerLayer.GetAdornerLayer(control);
        if (layer != null)
        {
            var adorner = new DragAdorner(container, control, e.GetPosition(control));
            var indicator = new DropIndicatorAdorner(control);
            // The snapshot first, the line over it: later is nearer the front, and on a strip the
            // snapshot is wide enough to cover the very line it is being aimed with.
            layer.Add(adorner);
            layer.Add(indicator);
            _drag[control] = (layer, adorner, indicator, container);
        }
        container.Opacity = 0.35;

        try
        {
            DragDrop.DoDragDrop(container, item, DragDropEffects.Move);
        }
        finally
        {
            // Removed rather than reset to a neutral value: leaving a stale entry behind (even with
            // onHandle/item cleared) is exactly what let a stray MouseMove right after this DoDragDrop
            // call resume as if still mid-drag, occasionally making the whole row draggable again until
            // the next real mouse-down. Only a fresh PreviewMouseLeftButtonDown may repopulate this.
            EndDrag(control);
        }
    }

    /// <summary>Takes down whatever a drag is currently painting, and undims the row it lifted.</summary>
    /// <remarks>
    /// Idempotent and safe to call for a control that is not dragging, which is what lets every way a
    /// drag can end -- finishing, the list going away, the window being hidden -- run the same cleanup
    /// rather than each having to know whether there was anything to clean.
    /// </remarks>
    private static void ClearAdorners(ItemsControl control)
    {
        if (!_drag.TryGetValue(control, out var d)) return;

        d.layer.Remove(d.adorner);
        d.layer.Remove(d.indicator);
        d.container.Opacity = 1.0;
        _drag.Remove(control);
    }

    private static void EndDrag(ItemsControl control)
    {
        ClearAdorners(control);
        _state.Remove(control);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var control = (ItemsControl)sender;
        if (!_drag.TryGetValue(control, out var d)) return;

        d.adorner.UpdatePosition(e.GetPosition(control));
        UpdateDropIndicator(control, d.indicator, e);
    }

    // Shows a line at the exact edge the row would land on if dropped right now -- computed with the
    // same oldIndex-vs-targetIndex comparison OnDrop itself uses, so the line never promises a landing
    // spot the actual drop wouldn't deliver.
    private static void UpdateDropIndicator(ItemsControl control, DropIndicatorAdorner indicator, DragEventArgs e)
    {
        if (!_state.TryGetValue(control, out var s) || s.item == null || control.ItemsSource is not IList list)
        {
            indicator.Update(0, 0, false);
            return;
        }

        var oldIndex = list.IndexOf(s.item);
        var targetContainer = FindContainer(e.OriginalSource as DependencyObject, control);
        var targetItem = targetContainer != null ? control.ItemContainerGenerator.ItemFromContainer(targetContainer) : null;
        var targetIndex = targetItem != null ? list.IndexOf(targetItem) : -1;

        if (targetContainer == null || targetIndex < 0 || targetIndex == oldIndex)
        {
            indicator.Update(0, 0, false);
            return;
        }

        // The trailing edge when the row is moving down/right, the leading edge when it is moving up/left
        // -- which is the edge it would actually come to rest against, either way.
        var horizontal = GetIsHorizontal(control);
        var far = oldIndex < targetIndex;
        var offset = horizontal
            ? targetContainer.TranslatePoint(new Point(far ? targetContainer.ActualWidth : 0, 0), control).X
            : targetContainer.TranslatePoint(new Point(0, far ? targetContainer.ActualHeight : 0), control).Y;

        indicator.Update(offset, horizontal ? control.ActualHeight : control.ActualWidth, true, horizontal);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        // _state[control] is cleared in OnPreviewMouseMove's own finally block once DoDragDrop
        // returns (which happens right after this handler runs), so this only reads it, never resets it.
        var control = (ItemsControl)sender;
        if (!_state.TryGetValue(control, out var s) || s.item == null) return;

        if (control.ItemsSource is not IList list) return;

        var oldIndex = list.IndexOf(s.item);
        if (oldIndex < 0) return;

        var targetContainer = FindContainer(e.OriginalSource as DependencyObject, control);
        var targetItem = targetContainer != null ? control.ItemContainerGenerator.ItemFromContainer(targetContainer) : null;
        var newIndex = targetItem != null ? list.IndexOf(targetItem) : list.Count - 1;

        if (newIndex < 0 || newIndex == oldIndex) return;

        // Reordering is a remove followed by an insert, and the remove takes the selection with it:
        // the selected object leaves the collection, so a Selector clears SelectedItem and a TwoWay
        // binding writes that null straight into the view model. Re-inserting does not undo it, which
        // is why a master/detail list (the plugin array editor, the quick panel's workspaces) went
        // blank on the right the moment a row was dragged. Restored explicitly below.
        var selector = control as Selector;
        var wasSelected = selector != null && ReferenceEquals(selector.SelectedItem, s.item);

        list.RemoveAt(oldIndex);
        list.Insert(newIndex, s.item);

        if (wasSelected && selector != null)
            selector.SelectedItem = s.item;
    }

    // A Button/TextBox press (Move Up/Down, Edit, Remove, ...) does not start a drag: IsHandle is meant
    // for otherwise-inert grip icons, and a template that puts one near a button should not turn that
    // button into a drag handle by accident.
    //
    // Unless the control IS the handle, which is checked first. A tab strip has no grip to put anywhere:
    // the tab is the handle, and the tab is a button. Marking it says so explicitly, and a press still
    // clicks it -- a drag only begins once the pointer has moved past the threshold, which a click by
    // definition does not.
    private static bool IsWithinHandle(DependencyObject? source, ItemsControl control)
    {
        while (source != null && source != control)
        {
            if (source is FrameworkElement fe && GetIsHandle(fe))
                return true;
            if (source is ButtonBase or TextBoxBase)
                return false;
            source = TreeWalk.Parent(source);
        }
        return false;
    }

    // Walks up from whatever was actually clicked/dropped on to the realized item container
    // ItemContainerGenerator knows about -- VirtualizingStackPanel means only currently-visible
    // containers exist at all, which is exactly what a live mouse event can ever land on anyway.
    //
    // Through TreeWalk, like the walk above: both start at an OriginalSource, and an item whose label
    // carries highlighted text hands one of these a Run, which is not a Visual at all.
    private static FrameworkElement? FindContainer(DependencyObject? source, ItemsControl control)
    {
        while (source != null && source != control)
        {
            if (source is FrameworkElement fe && control.ItemContainerGenerator.IndexFromContainer(fe) >= 0)
                return fe;
            source = TreeWalk.Parent(source);
        }
        return null;
    }

}
