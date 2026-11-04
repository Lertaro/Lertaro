using System.Windows;

using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;
using Lertaro.App.Views.Controls.Dialogs;

namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

// ITransientHostWindow: this exists to anchor a menu and dies with it, so no dialog may be owned to it
// -- see the interface for what that costs when it happens.
internal class MenuHelperWindow : Window, ITransientHostWindow
{
    // One window for the process, not one per right-click: each menu used to pay a fresh handle, render
    // target and foreground handoff, and leave a new HwndWrapper class GUID behind in every window
    // enumerator. A closed WPF window cannot be shown again, so this one is shown once and then only moved.
    private static MenuHelperWindow? _shared;

    private MenuHelperWindow()
    {
        Width = 1; Height = 1;
        WindowStyle = WindowStyle.None;
        // Shown, but only ever to be activated and to give the ContextMenu a placement target that outlives
        // it: nothing may be painted in it. A chromeless window still keeps WS_THICKFRAME while ResizeMode
        // stays at its CanResize default, and Win11 draws that frame as a rounded stroke with a shadow, so
        // around a 1x1 client area it read as a 13x13 hollow ring in the menu's own shadow gap (issue #468).
        // NoResize drops the frame, but the smallest a WPF window gets is still 2x2. Layered, transparent and
        // at zero opacity is the only state with no pixels at all: the window stays showable and activatable,
        // which is all this one is for.
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Opacity = 0;
        ShowInTaskbar = false; IsTabStop = false; Focusable = true;
        ThemedWindowIconHelper.Apply(this);
        Deactivated += (_, _) => LostActivation?.Invoke();
    }

    // Owned by whichever menu is using the window right now, cleared when that menu lets go: a handler left
    // behind by a previous menu would close the next one the first time it loses activation.
    internal Action? LostActivation { get; set; }

    /// <summary>Shows the shared anchor at a physical screen point, or moves it there when it is already up.</summary>
    internal static MenuHelperWindow Acquire(int physicalX, int physicalY)
    {
        var window = _shared ??= new MenuHelperWindow();
        if (!window.IsVisible)
        {
            // Parked before the first Show, like every other window here: Show hands the window to the OS,
            // which paints it at WPF's cascade position, and that position outlives the move into place in
            // every window enumerator.
            WindowParking.Park(window);
            window.Show();
        }
        WindowParking.PlaceAtPoint(window, physicalX, physicalY);
        return window;
    }

    /// <summary>Hands the anchor back to the parking row; it is not closed, because it is never created twice.</summary>
    internal static void Release(MenuHelperWindow window)
    {
        window.LostActivation = null;
        if (ReferenceEquals(_shared, window))
            WindowParking.Park(window);
    }
}
