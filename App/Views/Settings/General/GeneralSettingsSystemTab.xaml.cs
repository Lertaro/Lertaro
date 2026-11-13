using System.Windows.Navigation;

namespace Lertaro.App.Views.Settings.General;

public partial class GeneralSettingsSystemTab : System.Windows.Controls.UserControl
{
    public GeneralSettingsSystemTab() => InitializeComponent();

    // Same launcher the About page's own links use: a settings page's hyperlink must not open inside
    // the WPF app, and the default browser is what the token documentation belongs in.
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Helpers.UrlLauncher.Open(e.Uri);
        e.Handled = true;
    }
}
