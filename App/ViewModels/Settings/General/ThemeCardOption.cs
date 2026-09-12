using Lertaro.PluginSdk.Abstractions;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Lertaro.App.ViewModels.Settings.General;

/// <summary>A handful of representative brushes pulled out of a theme's own ResourceDictionary, used
/// to render a small mock quick-search-window preview card instead of a plain text dropdown entry.
/// Deliberately modeled on QuickSearchWindow (search box + result list) -- that floating window, not
/// the settings window, is the app's actual everyday UI.</summary>
public sealed class ThemeCardOption
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool IsDark { get; }
    public Brush Accent { get; }
    public Brush CardBg { get; }
    // The card's own border, not the currently active theme's -- using {DynamicResource BorderColor}
    // here would ring every card in whatever gray the ACTIVE theme happens to use, clashing with
    // whichever colorful theme each card is actually previewing.
    public Brush CardBorder { get; }
    public Brush SearchBg { get; }
    public Brush Text { get; }
    public Brush TextSecondary { get; }
    // Was ItemSelected: that key only ever fed this preview card, never the real result list (which
    // uses HoverBackground for its selected/hovered row, since hovering a row selects it -- see
    // ListBox.xaml's ResultItemStyle), so the two had drifted apart. Pulling from HoverBackground
    // instead makes the preview show exactly what QuickSearchWindow actually renders.
    public Brush SelectedRowBg { get; }
    // The same accent-bar brush the real result list uses to mark the selected row -- reused here so
    // the mock row in the preview mirrors the app's own selection treatment instead of inventing one.
    public Brush AccentBar { get; }
    // What to draw the selection checkmark in -- a theme's Accent can be light (e.g. Glacier's sky
    // blue), where a plain white glyph would be nearly invisible, so this reuses the same brush the
    // theme itself designates for text-on-accent contrast rather than assuming white always works.
    public Brush AccentText { get; }

    public ThemeCardOption(ITheme theme)
    {
        Id = theme.Id;
        DisplayName = theme.DisplayName;
        IsDark = theme.IsDark;
        var res = theme.GetResources();
        Accent = ResolveBrush(res, "AccentColor");
        CardBg = ResolveBrush(res, "CardBackground");
        CardBorder = ResolveBrush(res, "CardBorderBrush");
        SearchBg = ResolveBrush(res, "ControlBackground");
        Text = ResolveBrush(res, "TextPrimary");
        TextSecondary = ResolveBrush(res, "TextSecondary");
        SelectedRowBg = ResolveBrush(res, "HoverBackground");
        AccentBar = ResolveBrush(res, "AccentBarColor");
        AccentText = ResolveBrush(res, "PrimaryButtonText");
    }

    /// <summary>
    /// The theme's brush for <paramref name="key"/>, as a frozen copy.
    /// </summary>
    /// <remarks>
    /// Frozen matters, and not for thread-safety: an unfrozen Freezable used as a dependency-property
    /// value (every one of these becomes a <c>Fill</c>/<c>Background</c> on the preview card) gets an
    /// InheritanceContext back to the element holding it. That edge runs brush -> element, so as long as
    /// the brush itself is reachable, so is the whole visual tree it was painted into. These brushes are
    /// owned by the ThemeCardOption, which WPF's binding tables keep alive well past the settings
    /// window's own close -- so each preview card leaked its entire window (measured: one window per open,
    /// ~20MB each, never reclaimed until restart). A frozen Freezable has no inheritance context, so the
    /// edge does not exist and the window is collectable again.
    ///
    /// A copy rather than freezing the theme's own brush: those are shared with the rest of the UI, and
    /// freezing an object someone else may still mutate would throw. These values never change, so a
    /// frozen snapshot is all the preview card needs.
    /// </remarks>
    private static Brush ResolveBrush(System.Windows.ResourceDictionary res, string key)
    {
        if (!res.Contains(key) || res[key] is not Brush brush)
            return Brushes.Gray;

        if (brush.IsFrozen)
            return brush;

        var frozenCopy = brush.Clone();
        frozenCopy.Freeze();
        return frozenCopy;
    }
}
