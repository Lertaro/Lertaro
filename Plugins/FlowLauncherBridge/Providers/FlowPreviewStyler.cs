using System.Windows;
using System.Windows.Controls;
using Lertaro.PluginSdk.Services;
using LinqExpr = System.Linq.Expressions.Expression;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Enhances Flow.Launcher preview controls with modern typography, dark/light theme CSS,
/// slim scrollbars, and clean layout adjustments via reflection without tight assembly dependencies.
/// </summary>
public static class FlowPreviewStyler
{
    public static void ApplyStyling(UIElement? element)
    {
        if (element == null) return;

        if (element is UserControl uc)
        {
            CollapseRedundantBottomRow(uc);
            AttachWebViewStyling(uc);
        }
    }

    private static void CollapseRedundantBottomRow(UserControl uc)
    {
        if (uc.Content is Grid grid && grid.RowDefinitions.Count > 1)
        {
            foreach (UIElement child in grid.Children)
            {
                if (Grid.GetRow(child) == 1)
                {
                    child.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private static void AttachWebViewStyling(FrameworkElement parent)
    {
        var webView = FindWebView(parent);
        if (webView == null)
        {
            parent.Loaded += (s, e) =>
            {
                var wv = FindWebView(parent);
                if (wv != null) SetupWebView(wv);
            };
            return;
        }

        SetupWebView(webView);
    }

    private static void SetupWebView(FrameworkElement webView)
    {
        HookEvent(webView, "NavigationCompleted", () => InjectCss(webView));
        HookEvent(webView, "CoreWebView2InitializationCompleted", () =>
        {
            RegisterDocumentScript(webView);
            InjectCss(webView);
        });

        RegisterDocumentScript(webView);
        InjectCss(webView);
    }

    private static void HookEvent(object target, string eventName, Action callback)
    {
        try
        {
            var evt = target.GetType().GetEvent(eventName);
            if (evt == null || evt.EventHandlerType == null) return;

            var invokeMethod = evt.EventHandlerType.GetMethod("Invoke");
            if (invokeMethod == null) return;

            var parameters = invokeMethod.GetParameters()
                .Select(p => LinqExpr.Parameter(p.ParameterType, p.Name))
                .ToArray();

            var body = LinqExpr.Call(
                callback.Target != null ? LinqExpr.Constant(callback.Target) : null,
                callback.Method);

            var lambda = LinqExpr.Lambda(evt.EventHandlerType, body, parameters);
            var del = lambda.Compile();
            evt.AddEventHandler(target, del);
        }
        catch { }
    }

    private static void RegisterDocumentScript(FrameworkElement webView)
    {
        try
        {
            var core = webView.GetType().GetProperty("CoreWebView2")?.GetValue(webView);
            if (core == null) return;

            var script = BuildInjectionScript();
            var addScriptMethod = core.GetType().GetMethod("AddScriptToExecuteOnDocumentCreatedAsync", [typeof(string)]);
            if (addScriptMethod != null)
            {
                var taskObj = addScriptMethod.Invoke(core, [script]);
                if (taskObj is Task task)
                {
                    task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }
        catch { }
    }

    private static void InjectCss(FrameworkElement webView)
    {
        try
        {
            var core = webView.GetType().GetProperty("CoreWebView2")?.GetValue(webView);
            if (core == null) return;

            var script = BuildInjectionScript();
            var executeMethod = core.GetType().GetMethod("ExecuteScriptAsync", [typeof(string)]);
            if (executeMethod != null)
            {
                var taskObj = executeMethod.Invoke(core, [script]);
                if (taskObj is Task task)
                {
                    task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
        }
        catch { }
    }

    private static string BuildInjectionScript()
    {
        var isDark = ThemeService.IsDarkTheme;
        var textColor = isDark ? "#E6E6E6" : "#1F1F1F";
        var headColor = isDark ? "#FFFFFF" : "#000000";
        var posColor = isDark ? "#9CDCFE" : "#005FB8";
        var phonColor = isDark ? "#CE9178" : "#A31515";
        var numColor = isDark ? "#4EC9B0" : "#0E7A0D";
        var linkColor = isDark ? "#60CDFF" : "#0066CC";
        var scrollThumb = isDark ? "rgba(255,255,255,0.2)" : "rgba(0,0,0,0.2)";
        var scrollThumbHover = isDark ? "rgba(255,255,255,0.38)" : "rgba(0,0,0,0.38)";

        var css = $@"
            html, body {{
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Microsoft YaHei', 'PingFang SC', sans-serif !important;
                font-size: 14px !important;
                line-height: 1.65 !important;
                color: {textColor} !important;
                background-color: transparent !important;
                margin: 0 !important;
                padding: 10px 14px !important;
                word-break: break-word !important;
            }}
            body, p, span, div, font, table, tr, td, th, li, ul, ol, h1, h2, h3, h4, h5, h6 {{
                color: {textColor} !important;
                background-color: transparent !important;
            }}
            b, strong, h1, h2, h3, .DC, .YX, .headword, .word, .hw {{
                color: {headColor} !important;
                font-weight: 600 !important;
            }}
            i, em, .DX, .pos, .grammar {{
                color: {posColor} !important;
                font-style: italic !important;
            }}
            .CB, .phonetic, .pron, .ipa {{
                color: {phonColor} !important;
                font-family: 'Segoe UI', 'Lucida Sans Unicode', sans-serif !important;
            }}
            .entryNum, .entryDot {{
                color: {numColor} !important;
                font-weight: bold !important;
            }}
            a {{
                color: {linkColor} !important;
                text-decoration: none !important;
            }}
            a:hover {{
                text-decoration: underline !important;
            }}
            ::-webkit-scrollbar {{
                width: 6px !important;
                height: 6px !important;
            }}
            ::-webkit-scrollbar-track {{
                background: transparent !important;
            }}
            ::-webkit-scrollbar-thumb {{
                background: {scrollThumb} !important;
                border-radius: 3px !important;
            }}
            ::-webkit-scrollbar-thumb:hover {{
                background: {scrollThumbHover} !important;
            }}
        ".Replace("\r", " ").Replace("\n", " ");

        var jsonCss = System.Text.Json.JsonSerializer.Serialize(css);
        return $@"(function() {{
            var id = '__lertaro_theme_style__';
            var el = document.getElementById(id);
            if (!el) {{
                el = document.createElement('style');
                el.id = id;
                (document.head || document.documentElement).appendChild(el);
            }}
            el.textContent = {jsonCss};
        }})();";
    }

    private static FrameworkElement? FindWebView(DependencyObject? parent)
    {
        if (parent == null) return null;

        if (parent is FrameworkElement fe && fe.GetType().Name == "WebView2")
            return fe;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            var result = FindWebView(child);
            if (result != null) return result;
        }

        if (parent is UserControl uc && uc.Content is DependencyObject contentDep)
        {
            var result = FindWebView(contentDep);
            if (result != null) return result;
        }

        if (parent is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                var result = FindWebView(child);
                if (result != null) return result;
            }
        }

        return null;
    }
}
