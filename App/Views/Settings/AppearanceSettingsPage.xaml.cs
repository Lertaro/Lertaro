namespace Lertaro.App.Views.Settings;

public partial class AppearanceSettingsPage : System.Windows.Controls.UserControl
{
    public AppearanceSettingsPage() => InitializeComponent();

    // See ThemeCardContainerStyle's EventSetter: swallows the auto-scroll WPF triggers whenever a
    // theme card is selected (by click or programmatically), instead of letting it yank the whole
    // settings page to wherever that card happens to sit.
    private void ThemeCard_RequestBringIntoView(object sender, System.Windows.RequestBringIntoViewEventArgs e)
        => e.Handled = true;
}
