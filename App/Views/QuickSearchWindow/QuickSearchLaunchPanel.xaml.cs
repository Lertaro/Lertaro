using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.QuickSearchWindow;

public partial class QuickSearchLaunchPanel : WpfUserControl
{
    public QuickSearchLaunchPanel() => InitializeComponent();

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: AppSearchResult result }) return;
        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window) return;

        e.Handled = true;
        window.ExecuteFavorite(result);
    }
}
