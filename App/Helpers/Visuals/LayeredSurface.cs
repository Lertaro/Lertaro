using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Gives a borderless window the kind of surface its theme asks for, decided while the window still has no
/// handle to fix it in place.
/// </summary>
/// <remarks>
/// <see cref="Window.AllowsTransparency"/> cannot change once the handle exists, so this has to run at
/// construction time rather than on a theme switch. A theme at full opacity gets a plain window: its text keeps
/// ClearType and the window manager rounds its corners. A theme below full opacity gets the same layered window
/// the rest of the app uses, whose corner then has to be painted rather than clipped, because the window
/// manager does not round a layered one. A notification shown across a theme switch therefore keeps whichever
/// kind it started as until it goes away, which is the cheaper half of a two-frame window rebuild.
/// </remarks>
public static class LayeredSurface
{
    public static void Apply(Window window, Border surface)
    {
        var theme = ThemeManager.Instance.ActiveTheme;
        if (theme == null || theme.WindowOpacity >= 1.0)
        {
            // Rounded by the window manager, which needs a handle to be told about, hence the event rather
            // than a call here: it fires before the first paint, so the corners never visibly change.
            window.SourceInitialized += (_, _) => DwmWindowCorners.ApplyRound(window);
            return;
        }

        window.AllowsTransparency = true;
        window.Background = System.Windows.Media.Brushes.Transparent;
        surface.CornerRadius = (CornerRadius)window.FindResource("CornerRadiusWindow");
        // The clip is what makes the corner real here: a Border paints its own rounded fill but leaves its
        // children square, and only a layered window can show that flap at all, since an opaque one is
        // rounded by the window manager instead.
        RoundedClip.SetIsEnabled(surface, true);
        // The dim belongs on the content, not on Window.Opacity: a window-level opacity is the composited path,
        // and this window is already layered once for the theme's own transparency.
        WindowEffectHelper.ApplyThemeEffects(window, theme);
    }
}
