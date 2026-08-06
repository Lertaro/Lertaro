namespace Lertaro.App.Views.Settings.Plugins;

public partial class PluginManagementSettingsPage : System.Windows.Controls.UserControl
{
    public PluginManagementSettingsPage() => InitializeComponent();

    // Brings the selected plugin into view. Clicking a row needs no help, but the settings search
    // reveals a plugin by setting SelectedPlugin on the view model, and a selection arriving that way
    // lands wherever it already was: with more plugins than fit, searching for one silently selected it
    // off-screen and the page looked like it had ignored the result.
    private void PluginsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: not null } listBox)
            listBox.ScrollIntoView(listBox.SelectedItem);
    }

    private void SdkBadge_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.Settings.Plugins.PluginManagementViewModel vm && vm.DevGuideUri != null)
            Helpers.UrlLauncher.Open(vm.DevGuideUri);
    }
}
