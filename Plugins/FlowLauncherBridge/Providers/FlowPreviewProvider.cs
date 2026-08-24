using System.Windows;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Providers;

/// <summary>
/// Preview provider rendering Flow.Launcher plugin PreviewPanels inside Lertaro QuickLook window.
/// </summary>
public class FlowPreviewProvider : IFilePreviewProvider
{
    public string Name => TranslationService.Get("FlowLauncherBridge_PreviewProviderName");
    public string Description => TranslationService.Get("Plugin_Comp_Desc_FlowPreviewProvider");
    public int Priority => 1000;

    public bool CanPreview(string path, bool isDir)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return path.StartsWith("flow-preview:", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("__FLOW_PREVIEW__:", StringComparison.OrdinalIgnoreCase);
    }

    public UIElement CreatePreview(string path, bool isDir)
    {
        var preview = PluginPreviewCache.GetPreview(path);
        if (preview != null)
        {
            FlowPreviewStyler.ApplyStyling(preview);
            return preview;
        }

        var tb = new System.Windows.Controls.TextBlock
        {
            Text = path,
            Margin = new Thickness(12)
        };
        return tb;
    }
}
