using System.Text;
using System.Windows;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

/// <summary>
/// Borrows the topmost band from this app's own windows for the length of one native preview render, so a prompt the
/// handler raises as an owned window of them is ordered ABOVE its owner instead of underneath it -- Windows does not
/// keep a non-topmost owned window over a topmost owner, which is what makes such a prompt both invisible and the only
/// window holding the keyboard (see PreviewHandlerHost.Render).
/// Only windows that were already topmost are lowered, and exactly those get the band back.
/// </summary>
internal static class TopmostBand
{
    internal static List<Window> Suspend(Window? from)
    {
        var lowered = new List<Window>();
        for (var window = from; window != null; window = window.Owner)
        {
            if (!window.Topmost) continue;
            lowered.Add(window);
            window.Topmost = false;
        }
        // Named, because the restore's duration cannot tell a yielded window from none. Only logged when something
        // actually gave up the band, so a render of an already-handled file type stays quiet.
        if (lowered.Count > 0)
        {
            var names = new StringBuilder();
            foreach (var window in lowered)
                names.Append(names.Length == 0 ? "" : ",").Append(window.GetType().Name);
            Logger.Log($"[PreviewRender] topmost band yielded to [{names}]", LogLevel.Debug);
        }
        return lowered;
    }

    internal static void Restore(List<Window> lowered)
    {
        foreach (var window in lowered)
        {
            try { window.Topmost = true; } catch { }
        }
    }
}
