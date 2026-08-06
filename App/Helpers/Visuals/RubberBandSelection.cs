using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Drag across the empty space of a ListBox to select everything the box covers -- what a file manager
/// does, and what WPF's own Extended selection mode does not offer at all.
/// </summary>
/// <remarks>
/// Written for a tile view, where the items are small and scattered and reaching them one at a time is
/// the slow way. A list of full-width rows has almost no empty space to start from, so it costs nothing
/// to leave this attached in both.
///
/// A press on an item is left entirely alone: that is a click, or the beginning of a file drag out of
/// the list, and both already work. Only a press on nothing starts a band.
/// </remarks>
public static class RubberBandSelection
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(RubberBandSelection), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(ListBox list, bool value) => list.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(ListBox list) => (bool)list.GetValue(IsEnabledProperty);

    private sealed class Band
    {
        public required Point Start { get; init; }
        public required bool Additive { get; init; }
        public required bool[] WasSelected { get; init; }
        public AdornerLayer? Layer { get; set; }
        public RubberBandAdorner? Adorner { get; set; }
        public bool Active { get; set; }
    }

    // Keyed per list, not one shared field: every group in the quick panel renders its own ListBox and
    // they are all live at once.
    private static readonly Dictionary<ListBox, Band> _bands = new();

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;

        list.PreviewMouseLeftButtonDown -= OnDown;
        list.PreviewMouseMove -= OnMove;
        list.PreviewMouseLeftButtonUp -= OnUp;
        list.LostMouseCapture -= OnLostCapture;

        if (e.NewValue is not true) return;

        list.PreviewMouseLeftButtonDown += OnDown;
        list.PreviewMouseMove += OnMove;
        list.PreviewMouseLeftButtonUp += OnUp;
        list.LostMouseCapture += OnLostCapture;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (IsWithinItem(e.OriginalSource as DependencyObject, list)) return;

        _bands[list] = new Band
        {
            Start = e.GetPosition(list),
            // Ctrl keeps what was already selected and adds to it, the way it does everywhere else.
            Additive = (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            WasSelected = SelectionState(list),
        };

        // Captured on the press, not on the first move past the threshold. The list is also a file-drag
        // source (ResultsDragDropHelper), and that drag starts from whatever the pointer is over the
        // moment it moves far enough -- which mid-band is a tile. With the capture held, every move
        // reports this list as its source instead, so there is nothing for that handler to pick up.
        list.CaptureMouse();
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox list || !_bands.TryGetValue(list, out var band)) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            End(list);
            return;
        }

        var current = e.GetPosition(list);
        if (!band.Active)
        {
            // The same threshold a drag uses, so a press that wanders a pixel is still a click.
            if (Math.Abs(current.X - band.Start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - band.Start.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            band.Active = true;
            band.Layer = AdornerLayer.GetAdornerLayer(list);
            if (band.Layer != null)
            {
                band.Adorner = new RubberBandAdorner(list);
                band.Layer.Add(band.Adorner);
            }
        }

        var box = new Rect(band.Start, current);
        band.Adorner?.Update(box);
        Apply(list, box, band);
        e.Handled = true;
    }

    private static void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list) End(list);
    }

    // Losing the capture some other way (a window taking focus, Escape unwinding a modal loop) has to
    // take the adorner with it, or the box is left painted over a list nobody is dragging in.
    private static void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (sender is ListBox list) End(list);
    }

    private static void End(ListBox list)
    {
        if (!_bands.TryGetValue(list, out var band)) return;
        _bands.Remove(list);

        if (band.Adorner != null && band.Layer != null)
            band.Layer.Remove(band.Adorner);

        if (list.IsMouseCaptured)
            list.ReleaseMouseCapture();
    }

    private static void Apply(ListBox list, Rect box, Band band)
    {
        var bounds = new List<Rect>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
            bounds.Add(BoundsOf(list, i));

        var wanted = Resolve(box, bounds, band.WasSelected, band.Additive);
        for (var i = 0; i < wanted.Length; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container
                && container.IsSelected != wanted[i])
                container.IsSelected = wanted[i];
        }
    }

    /// <summary>Which items the box covers, given where they sit and what was selected before it began.</summary>
    /// <remarks>
    /// Touching counts, not containing: a box drawn across the middle of a row of tiles takes that row,
    /// which is what dragging across them looks like it should do. An item with no measured bounds (a
    /// container that does not exist) is empty, and an empty rectangle intersects nothing.
    /// </remarks>
    internal static bool[] Resolve(Rect box, IReadOnlyList<Rect> bounds, bool[] wasSelected, bool additive)
    {
        var wanted = new bool[bounds.Count];
        for (var i = 0; i < bounds.Count; i++)
        {
            var covered = !bounds[i].IsEmpty && box.IntersectsWith(bounds[i]);
            var kept = additive && i < wasSelected.Length && wasSelected[i];
            wanted[i] = covered || kept;
        }
        return wanted;
    }

    private static bool[] SelectionState(ListBox list)
    {
        var state = new bool[list.Items.Count];
        for (var i = 0; i < state.Length; i++)
            state[i] = list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem { IsSelected: true };
        return state;
    }

    private static Rect BoundsOf(ListBox list, int index)
    {
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container
            || !container.IsVisible)
            return Rect.Empty;

        try
        {
            var topLeft = container.TranslatePoint(new Point(0, 0), list);
            return new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // The container is not connected to this list's visual tree yet, which TranslatePoint
            // refuses rather than approximates.
            return Rect.Empty;
        }
    }

    // Walked through TreeWalk: this starts at a mouse event's OriginalSource, which is a Run whenever the
    // press landed on highlighted text, and VisualTreeHelper alone throws on one of those.
    private static bool IsWithinItem(DependencyObject? source, ListBox list)
    {
        while (source != null && source != list)
        {
            if (source is ListBoxItem) return true;
            source = TreeWalk.Parent(source);
        }
        return false;
    }
}
