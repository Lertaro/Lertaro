using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

/// <summary>The folder the user was last browsing, whichever application they were browsing in.</summary>
/// <remarks>
/// Not a folder anybody configured: it is wherever an Explorer window or a file dialog was last pointed,
/// which is why it cannot be a folder source and has to be a provider. A way back to what you were just
/// doing, one hotkey from anywhere, without having to remember the path.
///
/// The listing goes through DirectoryIndexerService rather than Directory.EnumerateFileSystemInfos: for
/// a drive the host indexes it costs no disk I/O at all, it falls back to a real walk for one it does
/// not, and it already leaves out hidden and system entries. The startup panel's version of this tab
/// walked the disk itself and re-implemented both of those filters.
/// </remarks>
public class LastDirectoryTabProvider : IQuickPanelTabProvider
{
    // A folder can hold far more than a tab should ever put on screen at once.
    private const int MaxItems = 100;

    public string Name => TranslationService.Get("QuickPanel_TabLastDirectory");

    public async Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var path = ExplorerPathService.GetLastActivePath();
        if (!IsWorthShowing(path)) return Array.Empty<ISearchResult>();

        var entries = new List<ISearchResult>();
        await foreach (var entry in DirectoryIndexerService
            .EnumerateDirectoryAsync(path!, recursive: false, limit: MaxItems, token: cancellationToken)
            .ConfigureAwait(false))
        {
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Whether that path is somewhere the user actually navigated to.</summary>
    /// <remarks>
    /// The Desktop is where Explorer and every file dialog land by default before anything more specific
    /// has been browsed to, so treating it as "last visited" would leave this tab permanently showing a
    /// location nobody chose.
    /// </remarks>
    internal static bool IsWorthShowing(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return !string.Equals(path.TrimEnd('\\'), desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}
