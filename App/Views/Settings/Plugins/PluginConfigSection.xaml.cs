// Both WinForms and WPF are referenced in this project, and both define UserControl.
using UserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.Settings.Plugins;

// Hosts the schema-driven plugin configuration field list.
public partial class PluginConfigSection : UserControl
{
    public PluginConfigSection() => InitializeComponent();
}
