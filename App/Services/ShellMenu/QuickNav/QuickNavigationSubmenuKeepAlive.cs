using System.Runtime.CompilerServices;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MouseEventHandler = System.Windows.Input.MouseEventHandler;
using Popup = System.Windows.Controls.Primitives.Popup;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Keeps one cascading quick-nav branch open while the pointer is inside (or heading into) it.
//
// WPF's MenuItem tears its own branch down off a timer: the instant the pointer leaves a row while it is
// still over the parent menu, MenuItem.MouseLeaveInMenuMode arms _closeHierarchyTimer for
// MenuShowDelay ms (via SetTimerToCloseHierarchy); when it ticks the row is deselected and
// OnIsSelectedChanged's IsSelected true->false branch sets IsSubmenuOpen = false. Cancelling that timer
// only ever happens on the MenuItem the pointer ENTERS (MouseEnterInMenuMode ends with
// StopTimer(ref _closeHierarchyTimer)) -- i.e. on a sibling row -- so nothing disarms it when the
// pointer's next stop is this row's cascade instead. Native Windows menus cover exactly this case with
// the diagonal-corridor rule (a child popup stays open while the pointer travels toward it); WPF has no
// equivalent. Result: moving across the few pixels between a row and its submenu closes that submenu
// under the cursor a beat later, whether or not the pointer made it inside -- #245, "cannot select a
// second-level folder", and the same at every level down (second to third, and so on).
//
// The close itself cannot be intercepted (the timer is private), so this repairs it instead:
// SubmenuClosed re-opens the branch when the pointer is demonstrably still inside it -- inside the rect
// the branch's popup occupied at the last pointer move over it, plus a small tolerance. Every way of
// genuinely dismissing a branch leaves the pointer somewhere else: picking a row closes the root menu
// first (the guard below) before this runs, and moving to a sibling row or out of the menu entirely
// leaves the pointer outside that rect.
internal static class QuickNavigationSubmenuKeepAlive
{
    // Menu.xaml's MenuItem template names the submenu host exactly this, matching every WPF theme.
    private const string SubmenuPopupPartName = "PART_Popup";

    // How far outside the branch's own bounds still counts as "aiming at it". Covers the pointer resting
    // on the popup's 1px border / rounded corner, and a pointer that has already eased a pixel or two
    // back off the edge. Deliberately small: this must not swallow a deliberate sideways move onto a
    // sibling row, which is a real dismissal.
    private const double Tolerance = 6;

    // Bound frame rate for re-opening the same branch. Plenty for recovering from one stray timer tick;
    // a tight cap so that anything closing it in a loop can never turn into an open/close storm on the
    // UI thread.
    private const int MaxReopensPerSecond = 5;

    private static readonly ConditionalWeakTable<MenuItem, BranchState> _branches = new();

    public static void Attach(MenuItem menuItem, ContextMenu contextMenu)
    {
        menuItem.SubmenuOpened += (s, e) =>
        {
            if (e.OriginalSource != menuItem) return;
            // Deferred past the popup's first layout pass: before that the template parts exist but the
            // child has no size yet, so there would be no bounds worth remembering.
            menuItem.Dispatcher.BeginInvoke(() => TrackPointerOverBranch(menuItem), System.Windows.Threading.DispatcherPriority.Loaded);
        };

        menuItem.SubmenuClosed += (s, e) => ReopenIfStillPointedAt(menuItem, contextMenu);
    }

    // Bound remembered on every pointer move inside the branch, not once when it opens: EnsureLoaded
    // swaps in the real children asynchronously, and the popup grows (sometimes more than once, via the
    // scroll-to-bottom continuation loader) long after it was first shown. Keeping the freshest rect is
    // what lets the check below tell "pointer is in there" from "pointer left to somewhere else".
    private static void TrackPointerOverBranch(MenuItem menuItem)
    {
        if (menuItem.Template.FindName(SubmenuPopupPartName, menuItem) is not Popup popup || popup.Child is not System.Windows.FrameworkElement child)
            return;

        RememberBounds(menuItem, child);

        MouseEventHandler? handler = null;
        handler = (s, e) =>
        {
            if (!popup.IsOpen)
            {
                child.MouseMove -= handler;
                return;
            }
            RememberBounds(menuItem, child);
        };
        child.MouseMove += handler;
    }

    private static void RememberBounds(MenuItem menuItem, System.Windows.FrameworkElement child)
    {
        var size = child.RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(child);
        var origin = child.PointToScreen(new System.Windows.Point(0, 0));

        // Screen space, so the pointer position below compares directly: PointToScreen already returns
        // device pixels, while RenderSize is DIPs and has to be scaled to match.
        _branches.GetOrCreateValue(menuItem).Bounds = new System.Windows.Rect(
            origin.X, origin.Y,
            size.Width * scale.DpiScaleX, size.Height * scale.DpiScaleY);
    }

    private static void ReopenIfStillPointedAt(MenuItem menuItem, ContextMenu contextMenu)
    {
        // The root closing is the one dismissal that takes this branch with it legitimately -- and every
        // item action does exactly that (triggerAction sets contextMenu.IsOpen = false first), so this
        // guard alone keeps "clicked something" out of the re-open path.
        if (!contextMenu.IsOpen) return;

        var branch = _branches.TryGetValue(menuItem, out var state) ? state : null;
        if (branch == null || !IsPointerOver(menuItem, branch.Bounds)) return;

        var now = DateTime.UtcNow;
        if (now - branch.LastReopenUtc > TimeSpan.FromSeconds(1)) branch.ReopenCount = 0;
        if (branch.ReopenCount >= MaxReopensPerSecond) return;
        branch.ReopenCount++;
        branch.LastReopenUtc = now;

        // Send priority: re-opened before the next render, so the close is never actually painted and
        // the branch looks like it simply stayed open. Items survive the round trip untouched (they are
        // still in menuItem.Items), so nothing reloads and no async rebuild is kicked off again.
        menuItem.Dispatcher.BeginInvoke(() =>
        {
            if (contextMenu.IsOpen && !menuItem.IsSubmenuOpen) menuItem.IsSubmenuOpen = true;
        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    private static bool IsPointerOver(MenuItem menuItem, System.Windows.Rect popupBounds)
    {
        var cursor = Cursor.Position;
        var pointer = new System.Windows.Point(cursor.X, cursor.Y);

        if (!popupBounds.IsEmpty && new System.Windows.Rect(popupBounds.X - Tolerance, popupBounds.Y - Tolerance,
                popupBounds.Width + Tolerance * 2, popupBounds.Height + Tolerance * 2).Contains(pointer))
            return true;

        // The parent row counts too: the pointer can still be sitting on it when something deselects the
        // row and takes the branch down with it, and that is never a dismissal either.
        var rowSize = menuItem.RenderSize;
        if (rowSize.Width <= 0 || rowSize.Height <= 0) return false;

        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(menuItem);
        var origin = menuItem.PointToScreen(new System.Windows.Point(0, 0));
        return new System.Windows.Rect(origin.X, origin.Y,
            rowSize.Width * scale.DpiScaleX, rowSize.Height * scale.DpiScaleY).Contains(pointer);
    }

    private sealed class BranchState
    {
        public System.Windows.Rect Bounds { get; set; }
        public DateTime LastReopenUtc { get; set; }
        public int ReopenCount { get; set; }
    }
}
