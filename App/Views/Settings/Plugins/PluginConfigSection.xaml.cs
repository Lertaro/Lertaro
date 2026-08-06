// Both WinForms and WPF are referenced in this project, and both define UserControl.
using UserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.Settings.Plugins;

// The plugin config editor as it appears inside a plugin's card. All of the behavior lives in the
// XAML's bindings and in FieldRowTemplate's own handlers, so there is nothing here but the load.
public partial class PluginConfigSection : UserControl
{
    public PluginConfigSection() => InitializeComponent();
}
