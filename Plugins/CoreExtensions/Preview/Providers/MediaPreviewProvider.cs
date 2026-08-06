using System.IO;
using System.Windows;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.Plugins.CoreExtensions.Preview.Controls;

namespace Lertaro.Plugins.CoreExtensions.Preview.Providers;

// Only the formats WPF's own MediaElement (Media Foundation under the hood) plays back on a stock
// Windows install without extra codec packs -- deliberately narrower than TypeFilterProvider's own
// VideoExts (which also lists .mkv/.flv/.webm, none of which MediaElement can open natively). A file
// whose container IS in this list but whose specific codec still isn't supported falls back to a static
// thumbnail via MediaPreviewControl's own MediaFailed handler, so listing a slightly optimistic set here
// costs nothing beyond that one file not auto-playing.
public class MediaPreviewProvider : IFilePreviewProvider
{
    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".wmv", ".avi", ".mov", ".mpg", ".mpeg"
    };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".wma", ".m4a", ".aac"
    };

    public string Name => TranslationService.Get("QuickLook_MediaProviderName");

    // Above ShellPreviewHandlerProvider (12) so this auto-playing control wins over whatever native
    // IPreviewHandler Windows might have registered for a media file, and above PePreviewProvider (15)'s
    // tier generally -- extensions never overlap between the two so exact tie-break order doesn't matter.
    public int Priority => 15;

    public static bool IsSupportedExtension(string ext) => VideoExts.Contains(ext) || AudioExts.Contains(ext);

    public bool CanPreview(string path, bool isDir) => !isDir && IsSupportedExtension(Path.GetExtension(path));

    public UIElement CreatePreview(string path, bool isDir) => new MediaPreviewControl(path);
}
