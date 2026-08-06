using System.Windows;

using Lertaro.App.Services.Theme;
using Lertaro.App.Views.Controls.Dialogs;
namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

// ITransientHostWindow: this exists to anchor a menu and dies with it, so no dialog may be owned to it
// -- see the interface for what that costs when it happens.
internal class MenuHelperWindow : Window, ITransientHostWindow
{
    public MenuHelperWindow(double x, double y)
    {
        Width = 1; Height = 1; Left = x; Top = y;
        WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false; IsTabStop = false; Focusable = true;
        ThemedWindowIconHelper.Apply(this);
    }
}
