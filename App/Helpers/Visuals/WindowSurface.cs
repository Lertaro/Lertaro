using System.Windows;
using System.Windows.Controls;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Applies opaque native window corners while preserving ClearType. Theme WindowOpacity remains in the SDK
/// for compatibility but is not applied to App windows.
///
/// For notification surfaces, match the painted radius to DWM's 8 DIP corners, or use zero when rounding
/// is unavailable. Windows managing their own painted radius should call DwmWindowCorners.ApplyRound
/// directly. Declare AllowsTransparency=false and an opaque Background before creating the HWND.
/// </summary>
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
