using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

/// <summary>The newest files across a set of watched folders, as one tab.</summary>
/// <remarks>
/// Distinct from a folder source set to "recently changed files", which a workspace can already hold:
/// this merges SEVERAL directories into one list by real modification time, so "what have I touched
/// lately" is one tab rather than one group per folder, each with its own separate newest-first order.
///
/// Its three settings live on this plugin's own config page (Settings > Plugins > Core Extensions)
/// rather than on the panel's, which is what moving out of the host and into a plugin means: the panel
/// knows about folders and about tabs, and nothing about what any one tab needs to be told.
/// </remarks>
public class RecentFilesTabProvider : IQuickPanelTabProvider
{
    internal const string PluginId = "Lertaro.Plugins.CoreExtensions";
    internal const string DirectoriesKey = "RecentFilesDirectories";
    internal const string CountKey = "RecentFilesCount";
    internal const string MaxAgeKey = "RecentFilesMaxAgeMinutes";

    public string Name => TranslationService.Get("QuickPanel_TabRecentFiles");

    public async Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var directories = PluginSettingsService
            .GetSetting(PluginId, DirectoriesKey, DefaultDirectories())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        // Nothing watched is a legitimate state, and the tab simply does not appear for it. The host
        // drops a provider that returns nothing, so there is nothing to configure for that.
        if (directories.Count == 0) return Array.Empty<ISearchResult>();

        var count = PluginSettingsService.GetSetting(PluginId, CountKey, 10);
        var maxAge = PluginSettingsService.GetSetting(PluginId, MaxAgeKey, 60);

        return await RecentFilesService
            .GetRecentFilesAsync(directories, count, maxAge, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The three folders things usually arrive in, which is what this tab is for.</summary>
    /// <remarks>
    /// Downloads is spelled out rather than taken from Environment.SpecialFolder, which has no entry for
    /// it: the folder postdates that enum. The same fallback the rest of this codebase uses.
    /// </remarks>
    internal static List<string> DefaultDirectories() => new()
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
    };
}
