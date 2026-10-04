using System.Windows;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.Search;
using MenuItem = System.Windows.Controls.MenuItem;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.QuickSearchWindow;

// The tab strip's context menu. Split out of QuickSearchLaunchPanel.xaml.cs, which sits just under the
// repo's per-file line limit.
public partial class QuickSearchLaunchPanel : WpfUserControl
{
    private void LaunchSourceSettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: LaunchPanelSourceViewModel source })
            return;

        // The manual tab has no data-source checkbox to land on -- it is the item list itself -- so it
        // opens the page; provider tabs jump straight to their own row in "数据源".
        if (string.Equals(source.Id, QuickLaunchSourceCatalog.ManualSourceId, StringComparison.OrdinalIgnoreCase))
            AppWindowManager.ShowSettingsWindow("QuickLaunch");
        else
            AppWindowManager.ShowSettingsQuickLaunchSource(source.Id);
    }
}
