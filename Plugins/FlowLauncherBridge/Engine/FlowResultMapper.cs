using System.IO;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Maps Flow.Launcher Result objects to Lertaro InstantResultItem objects.
/// </summary>
public static class FlowResultMapper
{
    public static List<InstantResultItem> MapToInstantResults(IEnumerable<Result> flowResults, FlowPluginHost? host = null) =>
        flowResults.Select(r => MapToInstantResult(r, host)).ToList();

    public static InstantResultItem MapToInstantResult(Result flowResult, FlowPluginHost? host = null)
    {
        var title = flowResult.Title ?? string.Empty;
        var description = flowResult.SubTitle ?? string.Empty;

        var actionArg = !string.IsNullOrEmpty(flowResult.CopyText) ? flowResult.CopyText : title;
        if (flowResult.PreviewPanel != null)
        {
            var pluginName = host?.GetAllPlugins().FirstOrDefault(p => p.Metadata.ID == flowResult.PluginID)?.Metadata.Name ?? "Flow Launcher Plugin";
            actionArg = PluginSdk.Services.PluginPreviewCache.Register(title, pluginName, flowResult.PreviewPanel);
        }

        var item = new InstantResultItem
        {
            Title = title,
            Description = description,
            TabCompletion = !string.IsNullOrEmpty(flowResult.AutoCompleteText) ? flowResult.AutoCompleteText : title,
            ActionArgument = actionArg,
            ActionType = "Execute"
        };

        // Resolve and render icon to HBITMAP
        if (flowResult.Icon != null)
        {
            try
            {
                var img = flowResult.Icon();
                if (img is System.Windows.Media.Imaging.BitmapSource bs)
                {
                    var hBitmap = FlowIconLoader.ConvertBitmapSourceToHBitmap(bs);
                    if (hBitmap != IntPtr.Zero)
                    {
                        item.HBitmapIcon = hBitmap;
                    }
                }
            }
            catch { }
        }

        if (item.HBitmapIcon == IntPtr.Zero)
        {
            var iconPath = !string.IsNullOrEmpty(flowResult.IcoPathAbsolute) && File.Exists(flowResult.IcoPathAbsolute)
                ? flowResult.IcoPathAbsolute
                : (!string.IsNullOrEmpty(flowResult.IcoPath) && File.Exists(flowResult.IcoPath) ? flowResult.IcoPath : null);

            if (!string.IsNullOrEmpty(iconPath))
            {
                var hBitmap = FlowIconLoader.LoadIconAsHBitmap(iconPath);
                if (hBitmap != IntPtr.Zero)
                {
                    item.HBitmapIcon = hBitmap;
                }
                else
                {
                    item.IconData = "path:" + iconPath;
                }
            }
        }

        // Map execution action
        item.OnExecute = () =>
        {
            try
            {
                var context = new ActionContext();
                if (flowResult.Action != null)
                {
                    flowResult.Action(context);
                }
                else if (flowResult.AsyncAction != null)
                {
                    _ = flowResult.AsyncAction(context);
                }
                else if (!string.IsNullOrEmpty(flowResult.CopyText))
                {
                    System.Windows.Clipboard.SetText(flowResult.CopyText);
                }
            }
            catch
            {
                // Suppress action callback errors
            }
        };

        return item;
    }
}
