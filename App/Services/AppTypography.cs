using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using FontFamily = System.Windows.Media.FontFamily;
using Control = System.Windows.Controls.Control;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ToolTip = System.Windows.Controls.ToolTip;

namespace Lertaro.App.Services;

internal static class AppTypography
{
    internal const string UiFontKey = "AppUiFontFamily";
    internal const string MonospaceFontKey = "AppMonospaceFontFamily";
    internal const string IconFontKey = "AppIconFontFamily";
    internal const string LanguageKey = "AppUiLanguage";
    internal const string UiFontWeightKey = "AppUiFontWeight";

    // WPF's built-in composite selects Windows fonts by script AND Language, including regional
    // CJK glyphs. Keep that maintained fallback table instead of ordering CJK families ourselves.
    internal static FontFamily UiFont { get; } = new("Segoe UI, Global User Interface");
    internal static FontFamily MonospaceFont { get; } = new("Consolas, Global User Interface");
    // Prefer Windows 11 icons; WPF falls back to the Windows 10 font when unavailable.
    internal static FontFamily IconFont { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static bool _registered;

    internal static void Initialize(ResourceDictionary resources, string? culture)
    {
        resources[UiFontKey] = UiFont;
        resources[MonospaceFontKey] = MonospaceFont;
        resources[IconFontKey] = IconFont;
        UpdateLanguage(resources, culture);
        if (_registered) return;
        _registered = true;

        // Popups have their own visual roots; covering Window alone misses menus/tooltips and
        // plugin-created popups. A root resource reference also updates already-open windows.
        foreach (var type in new[] { typeof(Window), typeof(Popup), typeof(ContextMenu), typeof(ToolTip) })
            EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnRootLoaded));

        // The dotted focus rectangle is not this app's visual language: every surface it styles already
        // nulls FocusVisualStyle by hand (list rows, text boxes, the search window's buttons, the sidebar,
        // the menus), so the rectangles that survive are on controls nobody wrote a style for rather than
        // on a cue anyone chose. Asked for once on every Control, because a per-type implicit style cannot
        // do this job: a control carrying an explicit keyed style ignores the implicit style entirely and
        // still takes the theme's rectangle for whatever property that style leaves alone.
        // What this gives up is real, and chosen: keyboard focus stops being visible on plain controls.
        // One RegisterClassHandler is all that has to go to bring the indication back.
        EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnControlLoaded));
    }

    private static void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded bubbles, so every control in the route hears it; act on the one that actually loaded.
        if (!ReferenceEquals(sender, e.OriginalSource)) return;
        var control = (Control)sender;
        // A local value means someone set it deliberately for this instance, which outranks the theme and
        // is not ours to overwrite.
        if (control.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty) != DependencyProperty.UnsetValue) return;
        control.SetValue(FrameworkElement.FocusVisualStyleProperty, null);
    }

    internal static void UpdateLanguage(ResourceDictionary resources, string? culture)
    {
        var language = ResolveLanguage(culture);
        resources[LanguageKey] = language;
        // Windows' message font can be lighter than 400 (305 on the affected system).
        // Regular and Normal both parse as 400, so anchor the Chinese UI's inherited weight explicitly.
        var isChinese = language.IetfLanguageTag == "zh" || language.IetfLanguageTag.StartsWith("zh-", StringComparison.Ordinal);
        resources[UiFontWeightKey] = isChinese ? FontWeights.Normal : System.Windows.SystemFonts.MessageFontWeight;
    }

    internal static XmlLanguage ResolveLanguage(string? culture)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(culture ?? "").Name;
            return XmlLanguage.GetLanguage(name.Length == 0 ? "en-US" : name);
        }
        catch (ArgumentException)
        {
            return XmlLanguage.GetLanguage("en-US");
        }
    }

    // The font sizes this app renders at, rounded up to the next rung. This used to carry a clarity
    // argument: the app forced TextFormattingMode.Display, which snaps every glyph advance to a whole
    // pixel, so a fractional size showed up as uneven spacing between letters. That forcing is gone (the
    // six places that measure a run with FormattedText all use the plain constructor, which is Ideal, so
    // the app was laying text out on metrics its own measuring code never asked for), and what is left
    // here is a size vocabulary: one ladder for every computed size, so two rows that should look alike
    // do, and nothing renders at a fractional em. Rounded UP rather than to nearest because the computed
    // size is the smallest the text was designed to stay legible at.
    internal static readonly double[] FontSizeLadder = [12, 14, 16, 18, 20, 24, 28, 32, 36];

    /// <summary>Quantises a computed font size onto <see cref="FontSizeLadder"/>. The result is always a
    /// whole number, which is the point: nothing downstream may render text at a fractional em.</summary>
    /// <remarks>
    /// Below the ladder's first rung the sizes are floors rather than choices -- the 9 DIP path text of a
    /// scaled-down result row is deliberately small -- so those are only made whole, and made whole means
    /// rounded up: the computed value is the smallest size the text was designed to stay legible at, so
    /// rounding down would put it under that floor. Past the top rung there is nothing left to round up to,
    /// and a whole number is all that is asked. Only a size that is not a number at all (an unset FontSize
    /// arriving as NaN) passes through untouched.
    /// </remarks>
    internal static double SnapFontSize(double size)
    {
        if (!double.IsFinite(size)) return size;
        if (size < FontSizeLadder[0]) return Math.Ceiling(size);
        foreach (var rung in FontSizeLadder)
        {
            if (size <= rung) return rung;
        }
        return Math.Ceiling(size);
    }

    private static void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (!ReferenceEquals(root, e.OriginalSource)) return;
        // exam/UI-improve.md section 四's rendering defaults, set on the root for the same reason the fonts
        // below are: both properties inherit, so a root is all that has to be told, and popups -- which are
        // separate visual roots -- get their own. Without this a line of text can be arranged on half a
        // pixel and every glyph in it is resampled, which looks exactly like a font problem and is not one.
        root.UseLayoutRounding = true;
        root.SnapsToDevicePixels = true;
        // Preserve an explicit content language or font supplied by a plugin. Descendant icon and
        // code fonts likewise take precedence over the inherited UI font.
        if (root.ReadLocalValue(TextElement.FontFamilyProperty) == DependencyProperty.UnsetValue)
            root.SetResourceReference(TextElement.FontFamilyProperty, UiFontKey);
        if (root.ReadLocalValue(FrameworkElement.LanguageProperty) == DependencyProperty.UnsetValue)
            root.SetResourceReference(FrameworkElement.LanguageProperty, LanguageKey);
        var weightSource = DependencyPropertyHelper.GetValueSource(root, TextElement.FontWeightProperty).BaseValueSource;
        if (weightSource is BaseValueSource.Default or BaseValueSource.Inherited
            or BaseValueSource.DefaultStyle or BaseValueSource.DefaultStyleTrigger)
            root.SetResourceReference(TextElement.FontWeightProperty, UiFontWeightKey);
    }
}
