using System.Windows;
using System.Windows.Controls;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Gives one of this app's borderless windows its surface: an opaque one, with corners that are the
/// window's real shape rather than a painting inside it.
/// </summary>
/// <remarks>
/// This used to be <c>LayeredSurface</c>, which also knew how to make a window layered when the active
/// theme asked for window transparency. That half is gone deliberately: a layered window cannot render
/// ClearType, so every theme's own <c>WindowOpacity</c> -- and the sub-pixel antialiasing with it -- was
/// being paid for by every window the user reads text in. The theme key and the
/// <see cref="Lertaro.PluginSdk.Abstractions.ITheme.WindowOpacity"/> member are still there so third-party
/// themes keep loading, but nothing in this app applies them to a window any more.
///
/// FOR THE NOTIFICATION SURFACES, whose radius this owns: it sets the card's radius to the 8 DIP the window
/// manager rounds by, and to zero where that is unavailable. A window that declares its own painted radius
/// instead should call <see cref="DwmWindowCorners.ApplyRound"/> directly rather than this -- otherwise the
/// two disagree and a nested <c>RoundedClip</c> clips children at a radius the surface no longer follows.
///
/// The window itself must already be declared opaque (<see cref="Window.AllowsTransparency"/> false, with
/// an opaque <see cref="Window.Background"/>) before this is called at construction time, because that
/// property cannot change once the handle exists.
/// </remarks>
public static class WindowSurface
{
    public static void Apply(Window window, Border surface)
    {
        // Both of these need the handle, which does not exist at the point a window declares its surface, and
        // SourceInitialized is where the compositor has a target for the window but has not drawn a frame yet.
        window.SourceInitialized += (_, _) =>
        {
            // Composed on the CPU: these windows exist to appear for a few seconds, so what is worth optimising
            // is the frame they arrive with (see CardRenderMode).
            CardRenderMode.Apply(window);
            // DWMWCP_ROUND uses an 8 DIP outer radius, independent of the theme's painted radius. Keep
            // surfaces on this contour; older Windows keeps square corners.
            surface.CornerRadius = new CornerRadius(DwmWindowCorners.ApplyRound(window) ? 8 : 0);
        };
    }
}
