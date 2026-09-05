using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FileFilters;

// Publishes the user-configured filters (keyword + folders + file pattern) as search scopes for the
// host's quick search: typing "<keyword> <term>" restricts a normal index search to those folders.
// Deliberately no filesystem access at all -- no registration, no watcher, no enumeration: the
// host's index answers scoped searches at query time (see FileFilterScopeResolver on the App side),
// which is what keeps a scope's memory flat no matter how big its folders are.
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
                    .Select(f => f?.Trim() ?? string.Empty)
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
