using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.CoreExtensions.Providers;

// Minimal ISearchResult implementation shared by the panel providers -- the startup panel's tabs and the
// quick panel's sources both just need to hand the host a path (plus an optional user-given display name
// for favorites, and a timestamp where the source has one).
internal sealed class PanelResultItem : ISearchResult
{
    public string Name { get; }
    public string FullPath { get; }
    public string ContextDirectory { get; }
    public bool IsDir { get; }
    public bool IsApplication { get; }

    /// <summary>
    /// Only ever the modified time, and only where the source knows one. The quick panel orders a group
    /// newest-first by it; the default value means "not known", which sorts last there and so leaves
    /// those entries in the order the provider returned them.
    /// </summary>
    public FileMetadata Metadata { get; }

    public PanelResultItem(
        string path,
        string? displayName = null,
        bool isApplication = false,
        DateTime modified = default,
        bool? isDirectory = null)
    {
        FullPath = path;
        IsApplication = isApplication;
        IsDir = !isApplication && (isDirectory ?? Directory.Exists(path));
        Name = string.IsNullOrWhiteSpace(displayName) ? DeriveName(path, isApplication) : displayName!;
        ContextDirectory = IsDir ? path : (Path.GetDirectoryName(path) ?? path);
        Metadata = new FileMetadata(0, default, modified, default);
    }

    private static string DeriveName(string path, bool isApplication)
    {
        // A packaged app's path is a virtual shell:AppsFolder\{AUMID} id, not a real filename --
        // Path.GetFileName on it would surface the raw AUMID, so resolve the shell's own friendly
        // display name instead (same fallback Favorites already uses for shell:/:: paths).
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.Ordinal))
            return ShellPathHelper.GetVirtualFolderDisplayName(path, path);

        var name = Path.GetFileName(path.TrimEnd('\\', '/'));

        // A classic Start Menu app's FullPath is its .lnk shortcut file itself (see
        // StartMenuAppItemProvider), whose own display name is the filename WITHOUT that extension
        // (Path.GetFileNameWithoutExtension) -- matches what the main results list shows for the same
        // app, instead of leaking the raw ".lnk" suffix into the name here.
        if (isApplication && name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);

        return string.IsNullOrEmpty(name) ? path : name;
    }
}
