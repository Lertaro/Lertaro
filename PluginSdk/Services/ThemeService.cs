using System.Windows;
using System.Windows.Media;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Provides global theme status to plugins without tight coupling to App internals.
/// </summary>
public static class ThemeService
{
    public static Func<bool>? IsDarkThemeFunc { get; set; }

    public static bool IsDarkTheme
    {
        get
        {
            if (IsDarkThemeFunc != null)
            {
                try { return IsDarkThemeFunc(); } catch { }
            }

            try
            {
                if (Application.Current?.Resources != null)
                {
                    var res = Application.Current.Resources;
                    if (res.Contains("IsDark") && res["IsDark"] is bool isDark)
                        return isDark;

                    if (res["CardBg"] is SolidColorBrush brush)
                    {
                        var c = brush.Color;
                        return ((c.R * 299 + c.G * 587 + c.B * 114) / 1000) < 128;
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
