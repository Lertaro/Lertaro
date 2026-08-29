using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.PluginSdk.Windows;

/// <summary>
/// Host-themed modal window frame for plugin-owned content.
/// </summary>
public partial class PluginWindow : Window
{
    public ContentControl ContentHostControl => ContentHost;

    public Panel Footer => FooterHost;

    public PluginWindow(
        string title,
        double width = 560,
        double height = 360,
        PluginWindowMode mode = PluginWindowMode.Window,
        ImageSource? icon = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        Width = width;
        Height = height;

        var effectiveIcon = icon ?? GetDefaultIcon();
        Icon = effectiveIcon;
        TitleBarLogo.Source = effectiveIcon;
        PluginWindowNativeBehavior.Configure(this, mode);
    }

    private static ImageSource? GetDefaultIcon()
    {
        var app = Application.Current;
        if (app?.MainWindow?.Icon is ImageSource mainIcon) return mainIcon;
        if (app?.Windows.OfType<Window>().FirstOrDefault(window => window.Icon != null)?.Icon is ImageSource existingIcon)
            return existingIcon;

        try
        {
            var fallback = new BitmapImage(new Uri(
                "pack://application:,,,/Lertaro.App;component/tray.png",
                UriKind.Absolute));
            fallback.Freeze();
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
