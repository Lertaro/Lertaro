using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
// WinForms is in scope through a global using here, and it has a ListBox of its own.
using ListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App.Views.QuickPanel;

// Moving the selection with the arrow keys, across groups. Split out of QuickPanelWindow.xaml.cs purely
// to keep that file under the repo's per-file line limit.
public partial class QuickPanelWindow
{
    /// <summary>Up and Down move the selection, across groups as well as within one.</summary>
    /// <remarks>
    /// Each group renders its own list, and a ListBox contains its own arrow keys by design: Down on the
    /// last row of a group did nothing at all, so the keyboard could only ever reach one group's worth of
    /// what was on screen. Here the groups are one sequence, top to bottom as they are drawn.
    ///
    /// A list that has the keyboard is left to move within itself first, and only what it refuses is
    /// taken over. That matters in tile view: WPF moves a grid selection to the tile BELOW, which knows
    /// about the row width, and stepping one item at a time instead would be wrong in a way no amount of
    /// arithmetic here would fix. So the boundary is not calculated, it is observed -- if the list did
    /// not move, it had nowhere to go, and the next group is where the key was meant to land.
    ///
    /// The filter box keeps the keyboard after a summon and never gives it up on its own, so from there
    /// the movement is this method's entirely -- and the selection moves without the box losing focus,
    /// exactly as the search windows behave, so typing can carry on after it.
    /// </remarks>
    private bool HandleSelectionKeys(System.Windows.Input.KeyEventArgs e)
    {
        // Not while the action flyout is up. It moves its own highlight with these very keys and hangs
        // its handler on this same window -- and this one is attached first (it is declared in the XAML),
        // so without standing down here the panel would eat the key and the open menu would sit there
        // looking dead. The same guard Escape already carries, for the same reason.
        if (Services.ShellMenu.ActionFlyout.ActionFlyout.IsOpen) return false;

        var delta = SelectionDeltaFor(e);
        if (delta == 0) return false;

        var focusedList = Keyboard.FocusedElement is DependencyObject focused
            ? Helpers.Visuals.TreeWalk.Ancestor<ListBox>(focused)
            : null;

        // A list has the keyboard and the key is one it moves on: its own answer first, this one only if
        // it had none. Left unhandled so the list still sees the key. A configured hotkey (Ctrl+N by
        // default) means nothing to a ListBox, so there is nothing to wait for and it is handled here.
        if (focusedList != null && Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down)
        {
            var before = focusedList.SelectedIndex;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ReferenceEquals(_activeList, focusedList) && focusedList.SelectedIndex == before)
                    MoveSelection(delta, takeFocus: true);
            }), System.Windows.Threading.DispatcherPriority.Input);
            return false;
        }

        // Either nothing holds the keyboard but the filter box -- where moving the selection deliberately
        // leaves it, so typing carries on afterwards -- or a list holds it and the key is one the list
        // has no use for.
        MoveSelection(delta, takeFocus: focusedList != null);
        e.Handled = true;
        return true;
    }

    /// <summary>+1 to move down the panel, -1 up, 0 for a key that is neither.</summary>
    /// <remarks>
    /// The arrows are literal, and the configurable next/previous-item hotkeys count too -- the same
    /// settings the quick window, the inline window, the actions flyout and the plugin context menu all
    /// read, so one binding moves a selection everywhere it can be moved. (The full window is the one
    /// surface that never picked them up, and honours only the bare arrows.)
    ///
    /// Which is why the caller runs this after the panel's action hotkeys rather than before: a plugin
    /// action bound to one of these keys wins, exactly as it does in the quick window, where the action
    /// dispatch likewise comes first.
    /// </remarks>
    private static int SelectionDeltaFor(System.Windows.Input.KeyEventArgs e)
    {
        var key = Helpers.WpfUiHelper.GetActualKey(e);
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (key == Key.Down) return 1;
            if (key == Key.Up) return -1;
        }

        var hotkeys = Core.UserSettings.Load().Hotkeys;
        if (Helpers.WpfUiHelper.MatchesHotkey(hotkeys.NextItemHotkey, Keyboard.Modifiers, key)) return 1;
        if (Helpers.WpfUiHelper.MatchesHotkey(hotkeys.PreviousItemHotkey, Keyboard.Modifiers, key)) return -1;

        return 0;
    }

    private bool MoveSelection(int delta, bool takeFocus)
    {
        var lists = VisibleLists(this);
        if (lists.Count == 0) return false;

        var from = _activeList != null ? lists.IndexOf(_activeList) : -1;
        var next = NextPosition(lists.Select(list => list.Items.Count).ToList(), from, from >= 0 ? lists[from].SelectedIndex : -1, delta);
        if (next == null) return false;

        var (listIndex, itemIndex) = next.Value;
        var target = lists[listIndex];

        // One selection across the panel, not one per group: a group left holding a highlighted row the
        // keyboard has since walked out of would read as two selections at once.
        foreach (var other in lists)
        {
            if (!ReferenceEquals(other, target)) other.UnselectAll();
        }

        _activeList = target;
        target.SelectedIndex = itemIndex;

        // The lists do not scroll -- the viewer around all the groups does -- so bringing the container
        // into view is what makes the panel follow the selection down past its own bottom edge.
        if (target.ItemContainerGenerator.ContainerFromIndex(itemIndex) is FrameworkElement container)
        {
            container.BringIntoView();
            if (takeFocus) (container as ListBoxItem)?.Focus();
        }

        return true;
    }

    /// <summary>Where one step lands, over the groups' items taken as a single sequence.</summary>
    /// <remarks>
    /// Groups with nothing in them are stepped over rather than stopped in: a filter can leave a group
    /// empty, and it is hidden when it does, so stopping there would be stopping on nothing.
    ///
    /// Neither end wraps. The tab strip wraps because its tabs are a ring the user is cycling; this is a
    /// list being read, and arriving back at the top from the bottom is how a selection gets lost.
    /// </remarks>
    internal static (int List, int Item)? NextPosition(IReadOnlyList<int> counts, int list, int item, int delta)
    {
        if (counts.Count == 0 || delta == 0) return null;

        // Nothing selected yet (or the group that held it is gone): start at the top either way. Down is
        // obvious; Up is the same answer because the alternative -- jumping to the very last entry -- is
        // a bigger surprise than not moving far enough.
        if (list < 0 || list >= counts.Count || item < 0)
            return FirstNonEmpty(counts, 0, 1);

        var within = item + delta;
        if (within >= 0 && within < counts[list]) return (list, within);

        for (var i = list + delta; i >= 0 && i < counts.Count; i += delta)
        {
            if (counts[i] == 0) continue;
            return (i, delta > 0 ? 0 : counts[i] - 1);
        }

        return null;
    }

    private static (int List, int Item)? FirstNonEmpty(IReadOnlyList<int> counts, int from, int step)
    {
        for (var i = from; i >= 0 && i < counts.Count; i += step)
        {
            if (counts[i] > 0) return (i, 0);
        }
        return null;
    }

    /// <summary>Every group's list that is actually on screen, in the order they are drawn.</summary>
    /// <remarks>
    /// Read off the visual tree rather than from the view model: a group the filter emptied and a group
    /// the user collapsed are both invisible, by two different mechanisms, and "is it visible" answers
    /// both at once without this having to know either.
    /// </remarks>
    private static List<ListBox> VisibleLists(DependencyObject root)
    {
        var found = new List<ListBox>();
        Collect(root, found);
        return found;

        static void Collect(DependencyObject node, List<ListBox> into)
        {
            if (node is ListBox { IsVisible: true } list)
            {
                into.Add(list);
                return;
            }

            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                Collect(System.Windows.Media.VisualTreeHelper.GetChild(node, i), into);
        }
    }
}
