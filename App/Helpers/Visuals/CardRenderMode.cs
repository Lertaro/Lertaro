using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Uses software composition for summoned cards to reduce first-frame latency. A measured 400-row tree
/// with a bitmap effect took 170–258 ms to paint under hardware composition. Software composition avoids
/// Direct3D surface setup/recreation and preserves ClearType.
///
/// The first present still requires an on-screen window; WindowParking shortens the visible gap. Cards
/// always use software composition. EnableHardwareAcceleration remains the user override for Settings,
/// Full Search, and Preview windows.
/// </summary>
internal static class CardRenderMode
{
    /// <summary>Switches one card window to software composition. Call it from SourceInitialized: by the time
    /// a window has been shown once, its hardware surface already exists and the mode only governs future
    /// frames, not the one that is about to be missed.</summary>
    internal static void Apply(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource source
            && source.CompositionTarget is HwndTarget target)
        {
            target.RenderMode = RenderMode.SoftwareOnly;
        }
    }
}
