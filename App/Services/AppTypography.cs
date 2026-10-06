using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using FontFamily = System.Windows.Media.FontFamily;
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

    private static void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        var root = (FrameworkElement)sender;
        if (!ReferenceEquals(root, e.OriginalSource)) return;
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
