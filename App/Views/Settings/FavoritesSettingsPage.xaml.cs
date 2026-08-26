using System.Windows;
using System.Windows.Controls.Primitives;

namespace Lertaro.App.Views.Settings;

public partial class FavoritesSettingsPage : System.Windows.Controls.UserControl
{
    public FavoritesSettingsPage() => InitializeComponent();

    private void OpenPresetMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu == null) return;

        // A ContextMenu attached to a Button only opens on right-click by default; the chevron
        // button must open it on a normal left-click too.
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }
}
