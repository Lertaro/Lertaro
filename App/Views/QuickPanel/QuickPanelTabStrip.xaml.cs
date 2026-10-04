using System.Windows.Input;

namespace Lertaro.App.Views.QuickPanel;

// See QuickPanelTabStrip.xaml for why this is a control of its own. UserControl is spelled out in full
// because System.Windows.Forms is also in scope in this project.
public partial class QuickPanelTabStrip : System.Windows.Controls.UserControl
{
    public QuickPanelTabStrip() => InitializeComponent();

    /// <summary>Rolling the wheel over the tabs moves along them, the way a browser's tab bar does.</summary>
    /// <remarks>
    /// The gesture the panel was missing: reaching the next workspace meant aiming at a tab a few pixels
    /// tall, while the wheel was already under the user's finger. It moves the SELECTION rather than
    /// scrolling the strip, which is the browser behaviour this is copying -- a strip that scrolled
    /// instead would leave the tabs you wanted off the edge and the one you were looking at unchanged.
    ///
    /// Marked handled either way, including at the ends: unhandled, a roll past the last tab would fall
    /// through to the group list and scroll the content, which after a run of tab switching reads as the
    /// panel suddenly jumping.
    ///
    /// Ctrl is deliberately not claimed here. That combination is the tile scale everywhere else in the
    /// panel (see QuickPanelWindow), and the window's own preview handler sees it first regardless, so
    /// this only has to not fight it.
    /// </remarks>
    private void Strip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not ViewModels.QuickPanel.QuickPanelViewModel viewModel) return;

        // One tab per notch, and the direction matches the roll: down (negative) moves forward through
        // the strip, which is the direction the content moves under the wheel.
        var index = IndexOfSelected(viewModel) + (e.Delta > 0 ? -1 : 1);
        if (index < 0 || index >= viewModel.Tabs.Count)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        _ = viewModel.SelectTabAtAsync(index + 1);
    }

    private static int IndexOfSelected(ViewModels.QuickPanel.QuickPanelViewModel viewModel)
    {
        for (var index = 0; index < viewModel.Tabs.Count; index++)
        {
            if (viewModel.Tabs[index].IsSelected) return index;
        }

        // Nothing selected yet, which a freshly built strip can be for the instant before the view model
        // settles: treat the roll as "from the start" rather than ignoring it.
        return 0;
    }
}
