using System.Windows;
using System.Windows.Media;
using Lertaro.App.Services;

namespace Lertaro.App.Views.QuickPanel;

// Which row the panel acts on, and how it is found. Split out of QuickPanelWindow.xaml.cs purely to keep
// that file under the repo's per-file line limit; every one of these operates on _activeList, which the
// input handlers there keep pointed at whichever list was last touched.
public partial class QuickPanelWindow
{
    /// <summary>Opens whatever the active list has selected. What a double-click does, and Enter with it.</summary>
    /// <remarks>
    /// One method rather than two identical ones, so the two ways of asking for the same thing cannot
    /// drift apart. Neither closes the panel: it is docked over the window you are working in, and the
    /// point of opening something from it is usually to open the next thing too.
    /// </remarks>
    private void OpenSelected()
    {
        if (_activeList?.SelectedItem is not AppSearchResult result) return;

        FileExecutor.OpenFileOrFolder(result.FullPath);
    }

    /// <summary>Puts the selection on the first entry of the first group that has one.</summary>
    /// <remarks>
    /// So a summon can be answered with Enter alone: focus lands in the filter box, typing narrows, and
    /// the thing being narrowed towards is already selected. Collapsed groups are skipped -- a filter
    /// that emptied a group hides it, and selecting inside something not on screen would leave Enter
    /// opening a file nobody could see was chosen.
    ///
    /// This also sets the list every hotkey acts on, which a summon otherwise leaves unset until the
    /// first click.
    /// </remarks>
    private void SelectFirstResult()
    {
        var list = FirstVisibleList(this);
        if (list == null || list.Items.Count == 0) return;

        list.SelectedIndex = 0;
        _activeList = list;
    }

    private static System.Windows.Controls.ListBox? FirstVisibleList(DependencyObject root)
    {
        if (root is System.Windows.Controls.ListBox { IsVisible: true } list) return list;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FirstVisibleList(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }
}
