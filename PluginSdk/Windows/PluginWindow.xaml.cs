using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.PluginSdk.Windows;

/// <summary>
/// Host-themed modal window frame for plugin-owned content.
/// </summary>
public partial class PluginWindow : Window
{
    private bool _showFooter = true;

    public ContentControl ContentHostControl => ContentHost;

    public Panel Footer => FooterHost;

    /// <summary>
    /// Whether the footer strip is reserved. Plugins that own no confirm/cancel actions can drop it and
    /// give the 52 rows back to their content. Defaults to true, so a plugin that never asks is unaffected.
    /// </summary>
    public bool ShowFooter
    {
        get => _showFooter;
        set
        {
            _showFooter = value;
            FooterBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            FooterRow.Height = value ? new GridLength(52) : GridLength.Auto;
        }
    }

    public PluginWindow(
        string title,
        double width = 560,
        double height = 360,
        PluginWindowMode mode = PluginWindowMode.Window,
        ImageSource? icon = null)
    {
        InitializeComponent();
        // The host's WindowSurface helper is what does this for the App's own windows, and PluginSdk
        // cannot reference it (App references this assembly, not the other way round), so the same two
        // lines are inline here: the window manager rounds the real window on Windows 11, and the card is
        // painted at that same 8 DIP contour. Older Windows has no such call, so the card is squared with
        // the square window rather than painted round inside it.
        SourceInitialized += (_, _) =>
            Surface.CornerRadius = new CornerRadius(ApplyRoundCorners(this) ? 8 : 0);
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

    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // No version gate: the attribute simply fails on an OS that has never heard of it (Windows 10), and
    // that failure is the answer this needs. A window with no handle yet cannot be asked at all.
    private static bool ApplyRoundCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var preference = DwmwcpRound;
        return DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int)) >= 0;
    }
}
