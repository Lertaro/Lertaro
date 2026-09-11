using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters;

// Publishes the user-configured filters (keyword + folders + file pattern) as search scopes for the
// host's quick search: typing "<keyword> <term>" restricts a normal index search to those folders.
// Deliberately no enumeration and no watcher -- the host's index answers scoped searches at query time
// (see FileFilterScopeResolver on the App side), which is what keeps a scope's memory flat no matter
// how big its folders are. The one thing a rebuild does touch is the shell, to turn configured entries
// into real paths (see ResolveFolder), and only when the config changes, never per keystroke.
public class FileFiltersScopeProvider : ISearchScopeProvider, IDisposable
{
    public string Name => TranslationService.Get("FileFilters_ProviderName");
    public string Description => TranslationService.Get("Plugin_Comp_Desc_FileFiltersScopeProvider");

    // Built lazily and cached; GetSearchScopes is consulted on every keystroke, and the config only
    // changes through PluginSettingsService (invalidation below). A torn read across threads costs
    // at most one redundant rebuild, so no lock.
    private IReadOnlyList<SearchScope>? _scopes;
    private int _disposed;

    public FileFiltersScopeProvider() => PluginSettingsService.SettingChanged += OnSettingChanged;

    public IReadOnlyList<SearchScope> GetSearchScopes()
    {
        if (_scopes != null)
            return _scopes;

        var scopes = new List<SearchScope>();
        // A null answer means "nothing persisted and no schema default reachable" -- resolving zero
        // scopes is the honest response either way.
        var filters = PluginSettingsService.GetSetting<List<FilterItem>>("Lertaro.Plugins.FileFilters", "Filters", null!);
        if (filters != null)
        {
            foreach (var filter in filters.Where(f => f.Enabled))
            {
                var keyword = filter.Keyword?.Trim() ?? string.Empty;
                var folders = (filter.Folders ?? new List<string>())
                    .Select(ResolveFolder)
                    .Where(f => f.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // A filter without a keyword can never be activated (no first token matches it), and
                // one without folders has nothing to search in. Folders are deliberately NOT
                // existence-checked here: an offline network folder must start working again the
                // moment it comes back, without a settings re-save, and the host's coverage check
                // decides what its index actually covers (logging a warning where it does not).
                if (keyword.Length == 0 || folders.Count == 0)
                    continue;

                scopes.Add(new SearchScope
                {
                    Keyword = keyword,
                    Folders = folders,
                    FilterPattern = string.IsNullOrWhiteSpace(filter.FilterPattern) ? "*" : filter.FilterPattern!
                });
            }
        }

        _scopes = scopes;
        return _scopes;
    }

    // The folder list is typed by hand, so it accepts the same two conveniences the rest of the app's
    // path fields do: "%VAR%" environment references, and Windows shell virtual folders ("shell:Downloads",
    // "::..."). Both have to be resolved here, because everything downstream -- the host's index-coverage
    // check and the engine's own directory filter -- works on a real path, and it normalizes one with
    // Path.GetFullPath: a "shell:" entry would become "<current directory>\shell:Downloads" instead of the
    // folder the user meant. ContentSearch's own NormalizeFolderPath does the same two steps for its
    // monitored folders.
    // Returns an empty string for an entry that is not a usable folder: blanks, and virtual paths the
    // shell could not turn into a physical folder (a non-filesystem one such as "shell:AppsFolder", or a
    // typo) -- no index can ever cover those, so passing one on would only make the host report it as a
    // "not covered by any index" folder, which is not what actually went wrong.
    internal static string ResolveFolder(string? folder)
    {
        var trimmed = folder?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return string.Empty;

        var resolved = ShellPathHelper.TryResolveVirtualPath(Environment.ExpandEnvironmentVariables(trimmed));
        if (!resolved.StartsWith("::", StringComparison.Ordinal)
            && !resolved.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            return resolved;

        Logger.Log($"[FileFilters] Folder '{trimmed}' could not be resolved to a real folder and is skipped.", LogLevel.Warn);
        return string.Empty;
    }

    private void OnSettingChanged(string pluginId, string key)
    {
        if (string.Equals(pluginId, "Lertaro.Plugins.FileFilters", StringComparison.OrdinalIgnoreCase)
            && string.Equals(key, "Filters", StringComparison.OrdinalIgnoreCase))
            _scopes = null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            PluginSettingsService.SettingChanged -= OnSettingChanged;
        GC.SuppressFinalize(this);
    }

    // Mirrors the config schema in FileFiltersPlugin.cs; the shape is what the settings UI's generic
    // array editor persists under the "Filters" key.
    public class FilterItem
    {
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;
        public string Keyword { get; set; } = string.Empty;
        public List<string> Folders { get; set; } = new();
        public string FilterPattern { get; set; } = "*";
    }
}
