using System.Runtime.InteropServices;
using System.Windows;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Lertaro.App.Views.Controls.Results;

/// <summary>
/// Selects a list result only after the pointer physically moves on screen.
/// </summary>
internal sealed class ResultsHoverSelection
{
    private readonly ListBox _list;
    private Point? _lastScreenPosition;

    public ResultsHoverSelection(ListBox list)
    {
        _list = list;
        _list.MouseMove += OnMouseMove;
        _list.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Reseed();
        };
    }

    public void Reseed()
        => _lastScreenPosition = TryGetScreenPosition(out var position) ? position : null;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!TryGetScreenPosition(out var position)
            || !UpdatePointerPosition(ref _lastScreenPosition, position))
        {
            return;
        }

        var item = ResultsControl.FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.Content != null && !ReferenceEquals(_list.SelectedItem, item.Content))
            _list.SelectedItem = item.Content;
    }

    internal static bool UpdatePointerPosition(ref Point? previous, Point current)
    {
        var moved = previous.HasValue && previous.Value != current;
        previous = current;
        return moved;
    }

    internal static bool TryGetScreenPosition(out Point position)
    {
        if (GetCursorPos(out var nativePosition))
        {
            position = new Point(nativePosition.X, nativePosition.Y);
            return true;
        }

        position = default;
        return false;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
