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
        WindowStyle = WindowStyle.None;
        // Shown, but only ever to be activated and to give the ContextMenu a placement target that outlives
        // it: nothing is painted in it. So it gets no layer, and there is no card for it to be opaque with
        // either -- a transparent background on a non-layered window paints black, so MenuBackground is the
        // least visible thing its one pixel can be, at the point the menu opens over.
        SetResourceReference(BackgroundProperty, "MenuBackground");
        ShowInTaskbar = false; IsTabStop = false; Focusable = true;
        ThemedWindowIconHelper.Apply(this);
    }
}
