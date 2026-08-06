using System.IO;
using System.Windows;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.Plugins.CoreExtensions.Preview.Providers;
namespace Lertaro.Plugins.CoreExtensions.Preview.Handlers;

// Previews any file type that has a registered Windows Preview Handler (PDF, Office, RTF, ...), using the
// real system preview instead of the metadata fallback. Auto-discovered by the plugin loader. Handlers are
// pooled (and their prevhost surrogates kept alive) for the preview session, then released together when
// the owning window closes (IPreviewSessionAware).
public class ShellPreviewHandlerProvider : IFilePreviewProvider, IPreviewSessionAware
{
    private readonly PreviewHandlerPool _pool = new();

    public string Name => TranslationService.Get("QuickLook_ShellPreviewProviderName");

    // Above the greedy text fallback (5) and folder (10), below image (20) / PE (15) so those keep their
    // fast self-drawn previews; this fills the gap for the rich formats they don't handle.
    public int Priority => 12;

    // Extensions already covered (and faster) by the lightweight built-in providers — leave them alone.
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico",
        ".txt", ".log", ".cs", ".xml", ".json", ".md", ".js", ".ts", ".py",
        ".html", ".css", ".ini", ".cfg", ".bat", ".cmd", ".sh", ".yml",
        ".yaml", ".sql", ".csproj", ".sln", ".config", ".properties",
        ".exe", ".dll"
    };

    public bool CanPreview(string path, bool isDir)
    {
        if (isDir || string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext) || Skip.Contains(ext)) return false;
        return PreviewHandlerRegistry.FindHandlerClsid(ext) != null;
    }

    public UIElement CreatePreview(string path, bool isDir)
    {
        var clsid = PreviewHandlerRegistry.FindHandlerClsid(Path.GetExtension(path));
        return clsid != null
            ? new PreviewHandlerHost(_pool, path, clsid.Value)
            : new DefaultMetadataPreviewProvider().CreatePreview(path, isDir);
    }

    // Session over (owner window closed): release the pooled handlers and their prevhost surrogates.
    public void EndPreviewSession() => _pool.ReleaseAll();
}
