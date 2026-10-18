using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Puts a summoned card's window on the software compositor, which is what its first frame is billed to.
/// </summary>
/// <remarks>
/// Measured (scratch/blackprobe, single monitor, hardware tier 2) on a card-shaped tree -- 400 themed rows and
/// a bitmap Effect -- the time from "the card is on screen where it belongs" to "the card has content" was
/// 170-258ms under hardware composition. That interval is the whole complaint: an opaque WPF window has no
/// pixels until its render thread presents a frame, and what fills the window meanwhile is the class
/// background, which is black. Software composition was faster in every shape measured here, not merely
/// cheaper-looking, and it takes away the two costs that make that interval long: the Direct3D device and
/// surface created on a window's first show, and the surface recreated whenever the size or the DPI context
/// changes -- which a card summoned onto a second monitor at another scale does on every summon. It shortens
/// the interval rather than removing it: the first present is gated on the window being on screen either way
/// (see WindowParking.ShowThenPlace). It also keeps ClearType, which is more than can be said for the
/// layered-window route this app abandoned.
///
/// This is deliberately not gated on <c>EnableHardwareAcceleration</c>: that setting is an opt-out for the
/// windows the user sits inside (Settings, the full search window, QuickLook), and it cannot be the reason a
/// 200ms unpainted gap appears on the windows the user only ever summons. Cards that appear and go away are
/// always software-composed; everything else still honors the setting.
/// </remarks>
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
