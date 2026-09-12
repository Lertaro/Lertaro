namespace Lertaro.App.Views.Settings.Plugins;

public partial class PluginManagementSettingsPage : System.Windows.Controls.UserControl
{
    public PluginManagementSettingsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.Settings.Plugins.PluginManagementViewModel oldVm)
            oldVm.PluginsReordered -= OnPluginsReordered;
        if (e.NewValue is ViewModels.Settings.Plugins.PluginManagementViewModel newVm)
            newVm.PluginsReordered += OnPluginsReordered;
    }

    // A reorder (sort toggle, or Apply/OK reconciling the sort after a toggle) keeps the selection on
    // the same VM but moves its row, which WPF does not scroll on its own -- bring it back into view so
    // the user is not left looking at whatever plugin happened to land where the old row was.
    private void OnPluginsReordered()
    {
        if (PluginsList.SelectedItem is null) return;
        // Background priority runs after layout AND render, and by then the virtualized ListBox has
        // regenerated its containers around the new item order -- scrolling earlier finds no container
        // for the moved row and silently no-ops, which is why the selection could still end up off-screen.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PluginsList.UpdateLayout();
            PluginsList.ScrollIntoView(PluginsList.SelectedItem);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

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

    private void PluginColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Single column header: click toggles between the default rank order and "disabled sink to
        // the bottom"; the header text follows the mode.
        if (DataContext is ViewModels.Settings.Plugins.PluginManagementViewModel vm)
            vm.TogglePluginSort();
    }
}
