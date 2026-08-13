using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Views.Controls.Results;

public static class ResultsDragDropHelper
{
    private static System.Windows.Point _dragStartPoint;
    private static bool _dragEndedInside;

    // True for the entire duration of the synchronous DoDragDrop call below (its own nested OLE
    // message loop). Checked by InlineSearchManager.CloseInlineSearch: that method can run
    // reentrantly from OTHER dispatcher-queued callbacks (e.g. the "click outside" mouse hook, which
    // arrives async via IPC from the separate Hook.exe process) that get pumped by OLE's loop while a
    // drag is still in flight -- destroying a window's HWND out from under a live DoDragDrop call
    // leaves the OS drag cursor permanently stuck (usually on "no-drop") because OLE never gets a
    // clean return through its own loop. See CloseInlineSearch's own comment for the retry side.
    public static bool IsDragActive { get; private set; }

    // When pressing on an item that's already part of a multi-selection, we suppress the list's
    // default "collapse to one" so a drag can carry all selected items. These remember the press
    // so a plain click (no drag) still collapses to the single item on button-up.
    private static object? _pendingItem;
    private static System.Windows.Controls.ListBox? _pendingList;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LBUTTON = 0x01;

    public static void Register(System.Windows.Controls.ListBox listBox)
    {
        listBox.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        listBox.PreviewMouseLeftButtonUp += List_PreviewMouseLeftButtonUp;
        listBox.PreviewMouseMove += List_PreviewMouseMove;
        // ponytail: register with handledEventsToo=true because the OLE system/ListBoxItem might handle it internally
        listBox.AddHandler(UIElement.QueryContinueDragEvent, new System.Windows.QueryContinueDragEventHandler(List_QueryContinueDrag), true);
    }

    /// <summary>
    /// Makes an element that isn't a results row drag the file it points at, exactly as dragging that
    /// row out of the list would -- same FileDrop payload, same drag effects, same hide-on-external-drop
    /// behaviour.
    /// </summary>
    /// <param name="element">The drag handle.</param>
    /// <param name="getPath">
    /// The path to drag, read at the moment the drag starts rather than when this is called, so a handle
    /// on something that re-points itself (the preview window's header following the selection) always
    /// carries whatever it is currently showing.
    /// </param>
    /// <remarks>
    /// Lives here rather than next to its caller so it shares the pieces of the list's own drag that were
    /// only learned by hitting them: the live GetAsyncKeyState check that keeps a stale WPF button state
    /// from starting a phantom drag, IsDragActive so a window isn't torn down under a running
    /// DoDragDrop, and the QueryContinueDrag handling that hides the search windows when the drop lands
    /// outside the process. A second, independent implementation of this would be a second chance to
    /// rediscover all of it.
    /// </remarks>
    public static void RegisterPathDragSource(FrameworkElement element, Func<string?> getPath)
    {
        element.PreviewMouseLeftButtonDown += (s, e) => _dragStartPoint = e.GetPosition(null);
        element.AddHandler(UIElement.QueryContinueDragEvent, new System.Windows.QueryContinueDragEventHandler(List_QueryContinueDrag), true);
        element.PreviewMouseMove += (s, e) =>
        {
            if (!ShouldStartDrag(e))
                return;

            var path = getPath();
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return;

            // One path, not a selection: the caller is a handle on a single thing. The list's own drag
            // carries every selected row because the row it starts from is one of them.
            StartDrag(element, new[] { path });
        };
    }

    // The distance threshold plus the authoritative hardware button check, shared so a second drag
    // source can't quietly skip either. See List_PreviewMouseMove for what the second one is for.
    private static bool ShouldStartDrag(System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return false;
        if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) == 0)
            return false;

        var diff = _dragStartPoint - e.GetPosition(null);
        return Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    // Explicit DataFormats.FileDrop with a string[] -- the format every shell drop target looks for.
    // Constructed with the format named rather than letting DataObject infer one from the value, so a
    // change to what is passed can't silently land under a format nothing reads.
    private static void StartDrag(DependencyObject dragSource, string[] paths)
    {
        var dataObject = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, paths);

        _pendingItem = null;
        _pendingList = null;
        _dragEndedInside = false;
        IsDragActive = true;
        try
        {
            DragDrop.DoDragDrop(dragSource, dataObject, System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Link);
        }
        finally
        {
            IsDragActive = false;
            if (!_dragEndedInside)
                ResultsDragDropWindowHelper.HideSearchWindows();
        }
    }

    private static void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _pendingItem = null;
        _pendingList = null;

        // No modifier + pressing a member of an existing multi-selection: keep the selection so a
        // drag carries all of it. Resolved as a single-select click on button-up if no drag runs.
        if (Keyboard.Modifiers == ModifierKeys.None && sender is System.Windows.Controls.ListBox lb)
        {
            var data = GetItemData(e.OriginalSource);
            if (data != null && lb.SelectedItems.Count > 1 && lb.SelectedItems.Contains(data))
            {
                e.Handled = true;
                _pendingItem = data;
                _pendingList = lb;
            }
        }
    }

    private static void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A suppressed press that never became a drag → treat as a plain click on the item.
        if (_pendingItem != null && _pendingList != null)
        {
            _pendingList.SelectedItem = _pendingItem;
            _pendingItem = null;
            _pendingList = null;
        }
    }

    private static object? GetItemData(object originalSource)
    {
        var dep = originalSource as DependencyObject;
        // Walk up via GetParent (not VisualTreeHelper.GetParent directly): the press can land on a
        // non-Visual ContentElement (e.g. a highlight Run inside the name TextBlock), which
        // VisualTreeHelper.GetParent rejects with InvalidOperationException. GetParent handles both.
        while (dep != null && dep is not ListBoxItem)
            dep = GetParent(dep);
        return (dep as ListBoxItem)?.DataContext;
    }

    // When the dragged item is part of a multi-selection, drag every selected file/folder.
    private static string[] CollectDragPaths(ItemsControl itemsControl, object dragged, string draggedPath)
    {
        var paths = new List<string>();
        if (itemsControl is System.Windows.Controls.ListBox lb && lb.SelectedItems.Count > 1 && lb.SelectedItems.Contains(dragged))
        {
            foreach (var obj in lb.SelectedItems)
            {
                try
                {
                    if (obj is ISearchResult result && PathExists(result))
                        paths.Add(result.FullPath);
                }
                catch { }
            }
        }
        if (paths.Count == 0) paths.Add(draggedPath);
        return paths.ToArray();
    }

    private static void List_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // ShouldStartDrag holds the distance threshold and, more importantly, the live hardware button
        // check: e.LeftButton (WPF's own tracked state) can end up stuck reporting Pressed during a plain
        // hover with no real down/up on this list at all -- when a press elsewhere in the inline window
        // has its window destroyed (CloseInlineSearch) before the matching release reaches any
        // Lertaro-owned window, WPF's state never resyncs, and the next hover starts a real, phantom
        // DoDragDrop with no hand left to ever release it.
        if (!ShouldStartDrag(e))
            return;

        if (sender is not ItemsControl itemsControl)
            return;

        var dep = (DependencyObject)e.OriginalSource;
        while (dep != null && dep != itemsControl)
        {
            if (dep is ListBoxItem item)
            {
                var data = item.DataContext;
                if (data != null)
                {
                    try
                    {
                        if (data is ISearchResult searchResult && PathExists(searchResult))
                        {
                            // A drag is starting — StartDrag also clears the pending press so button-up
                            // doesn't collapse the selection this drag is carrying.
                            StartDrag(item, CollectDragPaths(itemsControl, data, searchResult.FullPath));
                        }
                    }
                    catch { }
                }
                break;
            }
            dep = GetParent(dep);
        }
    }

    private static void List_QueryContinueDrag(object sender, System.Windows.QueryContinueDragEventArgs e)
    {
        var isLeftReleased = (e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0;

        // ponytail: Detect the exact millisecond when the user releases the mouse (either drop or cancel) or presses Escape.
        // We must check isLeftReleased because WPF's internal OleDragSource implementation returns directly to OLE
        // without raising the routed event for System.Windows.DragAction.Drop/Cancel.
        var isDragEnding = e.Action == System.Windows.DragAction.Drop ||
                            e.Action == System.Windows.DragAction.Cancel ||
                            isLeftReleased ||
                            e.EscapePressed;

        if (isDragEnding)
        {
            _dragEndedInside = ResultsDragDropWindowHelper.HandleDragEnding();
        }
    }

    private static bool PathExists(ISearchResult result) => PathExists(result, File.Exists, Directory.Exists);

    internal static bool PathExists(
        ISearchResult result,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists) =>
        !string.IsNullOrEmpty(result.FullPath) &&
        (result.IsDir ? directoryExists(result.FullPath) : fileExists(result.FullPath));

    private static DependencyObject? GetParent(DependencyObject dep)
    {
        if (dep is Visual || dep is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(dep);
        }
        else if (dep is FrameworkContentElement fce)
        {
            return fce.Parent;
        }
        return null;
    }
}
