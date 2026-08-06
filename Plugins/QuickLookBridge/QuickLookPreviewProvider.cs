using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.QuickLookBridge;

// Highest priority of any registered provider on purpose: whenever QuickLook is reachable, it wins for
// every file/folder, ahead of every built-in (image/text/media/shell-preview-handler/folder/PE) -- not
// just the ones nothing else covers. RendersExternally tells the host (QuickLookManager) to hide its own
// preview panel instead of showing CreatePreview's content -- the real preview is QuickLook's own floating
// window. IReceivesPreviewPanelBounds gets the exact screen rectangle that panel would have occupied, so
// QuickLookWindowPositioner can dock QuickLook's window there via plain SetWindowPos (a re-parenting/
// style-stripping approach was tried first and reverted: QuickLook was never designed to be embedded, and
// fighting its own window-management logic that way produced worse glitches than this simpler move-only
// approach does).
public class QuickLookPreviewProvider : IFilePreviewProvider, IPreviewSessionAware, IReceivesPreviewPanelBounds
{
    public string Name => TranslationService.Get("QuickLookBridge_ProviderName");

    // Above every built-in preview provider (the highest of which is ImagePreviewProvider at 20) so this
    // wins unconditionally whenever QuickLook is reachable, instead of only catching what nothing else
    // handles.
    public int Priority => 100;

    public bool RendersExternally => true;

    public bool CanPreview(string path, bool isDir)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var exists = isDir ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
        {
            Logger.Log($"[QuickLookBridge] CanPreview('{path}') => false (path doesn't exist)", LogLevel.Debug);
            return false;
        }

        var available = QuickLookPipeClient.IsAvailable();
        Logger.Log($"[QuickLookBridge] CanPreview('{path}') => {available}", LogLevel.Debug);
        return available;
    }

    // Never actually shown (RendersExternally=true short-circuits the host before this content would be
    // displayed) -- returns an IReusablePreview specifically so the host's own reuse check
    // (ReferenceEquals(provider, _currentProvider) && _currentPreview is IReusablePreview) short-circuits
    // on every subsequent navigation to another file this provider wins: without it, the host would
    // ReleasePreview() (which now also calls EndPreviewSession(), closing QuickLook's window) and
    // immediately CreatePreview() again on every single navigation, flickering QuickLook's window
    // closed-then-reopened instead of just updating in place.
    public UIElement CreatePreview(string path, bool isDir)
    {
        // Held until EndPreviewSession -- see PreviewActivationSignal's own doc comment; without this,
        // clicking into QuickLook's now-docked window (a separate process/top-level window) would read as
        // "the user switched away" to the quick window's foreground-loss auto-hide and dismiss it.
        PreviewActivationSignal.Begin();
        QuickLookPipeClient.TryInvokePreview(path);
        return new ExternalPreviewPlaceholder();
    }

    // The search window that was driving these previews just closed, or navigation moved to a file some
    // other provider handles -- either way tell QuickLook to hide its window too, instead of leaving it
    // floating on screen with nothing left pointing at it, and release the foreground-loss suppression.
    public void EndPreviewSession()
    {
        PreviewActivationSignal.End();
        QuickLookPipeClient.TryClosePreview();
    }

    public void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height) =>
        QuickLookWindowPositioner.DockTo(left, top, width, height);

    private sealed class ExternalPreviewPlaceholder : Grid, IReusablePreview
    {
        public bool TrySetTarget(string path, bool isDir)
        {
            QuickLookPipeClient.TryInvokePreview(path);
            return true;
        }
    }
}
