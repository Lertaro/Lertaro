using Lertaro.App.Services;

namespace Lertaro.App.Views.QuickPanel;

// The panel's side of the QuickLook preview. Split out of QuickPanelWindow.xaml.cs purely to keep that
// file under the repo's per-file line limit; the key that gets here is decided there, alongside the
// panel's other keys, and read from the settings rather than spelled out (see SearchInputHelper).
public partial class QuickPanelWindow
{
    /// <summary>Opens the preview on the selected row, or closes it. Answers whether it did anything.</summary>
    /// <remarks>
    /// The same manager, and the same toggle, the search windows use -- so a preview belongs to one
    /// window at a time and the panel does not need its own idea of what a preview is. A row with
    /// nothing to preview (a plugin's non-file entry) leaves the key unhandled, which lets it fall
    /// through to whatever else is bound to it.
    /// </remarks>
    private bool TogglePreview()
    {
        if (_activeList?.SelectedItem is not AppSearchResult { CanPreview: true } result) return false;

        QuickLookManager.Instance.Toggle(this, result.FullPath);
        return true;
    }

    /// <summary>Keeps an open preview on whatever is selected now.</summary>
    /// <remarks>
    /// Only ever updates a preview the user already asked for: UpdateOrShow does nothing while none is
    /// wanted, so an ordinary click does not summon one. Wired per list rather than once for the window,
    /// because each group renders its own -- and a selection landing in one group means the other groups
    /// are dropping theirs, which arrives here as a list with no selection at all. That is a group
    /// letting go, not a row that cannot be previewed, so it must not close anything.
    /// </remarks>
    private void GroupList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        if (list.SelectedItem is AppSearchResult { CanPreview: true } result)
        {
            _activeList = list;
            QuickLookManager.Instance.UpdateOrShow(this, result.FullPath);
        }
        else if (ReferenceEquals(list, _activeList) && list.SelectedItem != null)
        {
            QuickLookManager.Instance.HideFrom(this);
        }
    }

    /// <summary>Takes the preview down with the panel.</summary>
    /// <remarks>
    /// The preview is this summon's, like everything else the panel remembers. WPF closes the window
    /// itself along with this one (it is an owned window), but Reset also clears the "the user wants a
    /// preview" flag -- without which the next summon would open one nobody asked for, on whatever its
    /// first selection happened to be.
    /// </remarks>
    private static void ReleasePreview() => QuickLookManager.Instance.Reset();
}
