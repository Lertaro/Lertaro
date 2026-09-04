using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.Services.Plugin;

/// <summary>
/// Central hub for plugin lifecycle management: loading, registration,
/// filtering by enabled state, search action dispatch, and instant result execution.
/// <para>
/// Loading is delegated to <see cref="PluginLoader"/>;
/// component enable/disable state is managed by <see cref="ComponentFilter"/>.
/// </para>
/// </summary>
public class PluginManager : PluginRegistry
{
    private static readonly Lazy<PluginManager> _instance = new(() => new PluginManager());

    /// <summary>Gets the singleton instance of the PluginManager.</summary>
    public static PluginManager Instance => _instance.Value;

    private readonly List<PluginSdk.Abstractions.Plugins.IPlugin> _plugins = new();
    private readonly List<PluginActionRegistration> _actions = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> _dynamicActionProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IInstantResultProvider> _instantResultProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider> _fullSearchFileResultProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> _searchableItemProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> _sidebarFilterProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IResultColumnProvider> _resultColumnProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ITranslationProvider> _translationProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IThemeProvider> _themeProviders = new();
    private readonly List<IActivePathCollector> _pathCollectors = new();
    private readonly List<IFilePreviewProvider> _previewProviders = new();
    private readonly List<IQuickNavigationProvider> _quickNavigationProviders = new();
    private readonly List<IThumbnailProvider> _thumbnailProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> _queryTokenProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider> _quickPanelTabProviders = new();
    private readonly List<PluginSdk.Abstractions.Plugins.ISearchScopeProvider> _searchScopeProviders = new();
    private uint _nextRuntimeActionId = 0x80000000;

    // pluginId -> (field Key -> schema DefaultValue), built once after all plugins are loaded, so
    // GetSettingFunc can fall back to a plugin's own declared default without every call site needing
    // to duplicate it in code. See PluginLoaderHelper.BuildSchemaDefaultsMap.
    private Dictionary<string, Dictionary<string, object?>> _pluginSchemaDefaults = new(StringComparer.OrdinalIgnoreCase);

    private readonly ComponentFilter _filter = new();
    private readonly PluginManagerOrderedProviders _orderedProviders;

    private PluginManager()
    {
        _filter.Refresh();
        _orderedProviders = new PluginManagerOrderedProviders(
            this, _filter, _quickNavigationProviders, _sidebarFilterProviders, _resultColumnProviders,
            _previewProviders, _thumbnailProviders);

        // Wire up the dynamic filtering delegate for alias providers in the Core indexer
        AliasProviderRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.AliasProvider, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for active path collectors
        PluginSdk.Registries.ActivePathCollectorRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.ActivePathCollector, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for file dialog adapters
        PluginSdk.Registries.FileDialogAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.FileDialogAdapter, prov.GetType().Name);

        // Wire up the dynamic filtering delegate for inline search adapters
        PluginSdk.Registries.InlineSearchAdapterRegistry.FilterFunc = prov =>
            _filter.IsEnabled(ComponentFilter.GetDllName(prov), PluginComponentType.InlineSearchAdapter, prov.GetType().Name);

        // Bridges PluginSdk's own static service delegates (settings, history, favorites, fuzzy-match,
        // highlight-mask, directory search) to their Core/App implementations -- none of that is plugin
        // lifecycle itself, so it lives in its own class rather than this constructor.
        PluginSdkBridge.Initialize(this);

        PluginLoader.Load(this);

        // Must run after PluginLoader.Load so every plugin's IConfigurable is discoverable; must pass
        // `this` rather than PluginManager.Instance since Instance's Lazy<T> is still initializing here.
        _pluginSchemaDefaults = Helpers.PluginLoaderHelper.BuildSchemaDefaultsMap(this);
    }

    // ── PluginRegistry callbacks ──────────────────────────────────────────

    void PluginRegistry.RegisterPlugin(PluginSdk.Abstractions.Plugins.IPlugin plugin) => RegisterPlugin(plugin);

    void PluginRegistry.AddInstantResultProvider(PluginSdk.Abstractions.Plugins.IInstantResultProvider p) => _instantResultProviders.Add(p);
    void PluginRegistry.AddFullSearchFileResultProvider(PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider p) => _fullSearchFileResultProviders.Add(p);
    void PluginRegistry.AddSearchableItemProvider(PluginSdk.Abstractions.Plugins.ISearchableItemProvider p) => _searchableItemProviders.Add(p);
    void PluginRegistry.AddSidebarFilterProvider(PluginSdk.Abstractions.Plugins.ISidebarFilterProvider p) => _sidebarFilterProviders.Add(p);
    void PluginRegistry.AddResultColumnProvider(PluginSdk.Abstractions.Plugins.IResultColumnProvider p) => _resultColumnProviders.Add(p);
    void PluginRegistry.AddTranslationProvider(PluginSdk.Abstractions.Plugins.ITranslationProvider p) => _translationProviders.Add(p);
    void PluginRegistry.AddThemeProvider(PluginSdk.Abstractions.Plugins.IThemeProvider p) => _themeProviders.Add(p);
    void PluginRegistry.AddActivePathCollector(IActivePathCollector p)
    {
        _pathCollectors.Add(p);
        PluginSdk.Registries.ActivePathCollectorRegistry.Register(p);
    }
    void PluginRegistry.AddFilePreviewProvider(IFilePreviewProvider p) => _previewProviders.Add(p);
    void PluginRegistry.AddQuickNavigationProvider(IQuickNavigationProvider p) => _quickNavigationProviders.Add(p);
    void PluginRegistry.AddThumbnailProvider(IThumbnailProvider p) => _thumbnailProviders.Add(p);
    void PluginRegistry.AddQueryTokenProvider(PluginSdk.Abstractions.Plugins.IQueryTokenProvider p) => _queryTokenProviders.Add(p);
    void PluginRegistry.AddQuickPanelTabProvider(PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider p) => _quickPanelTabProviders.Add(p);
    void PluginRegistry.AddSearchScopeProvider(PluginSdk.Abstractions.Plugins.ISearchScopeProvider p) => _searchScopeProviders.Add(p);

    // ── Public API ────────────────────────────────────────────────────────

    // Backs PluginSdkBridge's PluginSettingsService.GetSettingFunc wiring: falls back to a plugin's own
    // schema-declared DefaultValue (see _pluginSchemaDefaults) when nothing has been persisted yet,
    // before falling back to whatever default the call site itself passed in -- so a plugin's config
    // schema is the single source of truth for its defaults instead of needing a second hardcoded copy
    // in code for the "never opened settings" case.
    internal object? GetPluginSetting(string pluginId, string key, object? defaultValue)
    {
        var settings = UserSettings.Load();
        if (settings.PluginSettings.TryGetValue(pluginId, out var pluginDict) && pluginDict.ContainsKey(key))
        {
            return settings.GetPluginSetting(pluginId, key, defaultValue);
        }
        if (_pluginSchemaDefaults.TryGetValue(pluginId, out var fieldDefaults) && fieldDefaults.TryGetValue(key, out var schemaDefault))
        {
            return schemaDefault;
        }
        return defaultValue;
    }

    // Backs PluginSdkBridge's PluginSettingsService.SetSettingFunc wiring -- a plugin writing its own
    // setting back at runtime (as opposed to the Settings UI's own batched apply/save flow), so this
    // saves immediately rather than waiting for anything else to trigger a save.
    internal void SetPluginSetting(string pluginId, string key, object? value)
    {
        var settings = UserSettings.Load();
        // Normalized to a JsonElement -- the same shape a fresh disk reload of UserSettings would
        // produce for this value (System.Text.Json deserializes an `object`-typed property as
        // JsonElement). Without this, a plugin passing a strongly-typed object (e.g. a
        // List<SomePocoClass>) leaves a shape in memory that the Settings UI's own generic
        // Dictionary/JsonElement-based readers (ConfigValueHelper.UnpackValue, used by
        // PluginConfigArrayFieldSupport to populate each array row's fields) can't read field-by-field --
        // the row appears but every field in it shows blank until the app restarts and reloads from disk.
        object? normalized = value == null ? null : System.Text.Json.JsonSerializer.SerializeToElement(value);
        settings.SetPluginSetting(pluginId, key, normalized);
        settings.Save();
    }

    // Raised so callers that cache anything derived from IsEnabled-filtered collections (e.g.
    // QuickPanelTabProviders, which RefreshDisabledComponents can change the membership of) know to
    // invalidate -- see App.xaml.cs's SettingsSearchService.GetEntriesFunc cache.
    public event Action? ComponentsRefreshed;

    public void RefreshDisabledComponents()
    {
        _filter.Refresh();
        ComponentsRefreshed?.Invoke();
        PluginSdk.Services.PluginSettingsService.NotifyComponentEnablementChanged();
    }

    public bool IsComponentEnabled(string dllName, PluginComponentType type, string name)
        => _filter.IsEnabled(dllName, type, name);

    /// <summary>Registers a plugin and loads its actions and dynamic providers.</summary>
    public void RegisterPlugin(PluginSdk.Abstractions.Plugins.IPlugin plugin)
    {
        if (plugin == null) return;
        _plugins.Add(plugin);
        if (plugin is PluginSdk.Abstractions.Plugins.IActionProvider actionProvider)
        {
            foreach (var action in PluginPerformanceMonitor.Measure(plugin,
                         () => actionProvider.GetActions()?.ToList() ?? new List<PluginSdk.Abstractions.ISearchResultAction>()))
                _actions.Add(new PluginActionRegistration(_nextRuntimeActionId++, plugin, action));
            foreach (var provider in PluginPerformanceMonitor.Measure(plugin,
                         () => actionProvider.GetDynamicActionProviders()?.ToList() ?? new List<PluginSdk.Abstractions.Plugins.IDynamicActionProvider>()))
                _dynamicActionProviders.Add(provider);
        }
    }

    // ── Filtered collections (active components only) ─────────────────────

    public IEnumerable<PluginSdk.Abstractions.Plugins.IPlugin> Plugins => _plugins;

    public IEnumerable<PluginActionRegistration> Actions
        => _actions.Where(a => _filter.IsEnabled(ComponentFilter.GetDllName(a.Plugin), PluginComponentType.Action, a.Action.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> DynamicActionProviders
        => _dynamicActionProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.DynamicActionProvider, p.GetType().Name));

    public IEnumerable<IQuickNavigationProvider> QuickNavigationProviders
        => _orderedProviders.QuickNavigationProviders;

    public IEnumerable<IQuickNavigationProvider> AllQuickNavigationProviders => _quickNavigationProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> InstantResultProviders
        => _instantResultProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.InstantProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider> FullSearchFileResultProviders
        => _fullSearchFileResultProviders.Where(p => _filter.IsEnabled(
            ComponentFilter.GetDllName(p), PluginComponentType.FullSearchFileResultProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> SearchableItemProviders
        => _searchableItemProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.SearchableItemProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> SidebarFilterProviders
        => _orderedProviders.SidebarFilterProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.IResultColumnProvider> ResultColumnProviders
        => _orderedProviders.ResultColumnProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.ITranslationProvider> TranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IThemeProvider> ThemeProviders => _themeProviders;
    public IEnumerable<IActivePathCollector> ActivePathCollectors => _pathCollectors;
    public IEnumerable<IFilePreviewProvider> FilePreviewProviders
        => _orderedProviders.FilePreviewProviders;

    public IEnumerable<IThumbnailProvider> ThumbnailProviders
        => _orderedProviders.ThumbnailProviders;

    public IEnumerable<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> QueryTokenProviders
        => _queryTokenProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QueryTokenProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider> QuickPanelTabProviders
        => _quickPanelTabProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.QuickPanelTabProvider, p.GetType().Name));

    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchScopeProvider> SearchScopeProviders
        => _searchScopeProviders.Where(p => _filter.IsEnabled(ComponentFilter.GetDllName(p), PluginComponentType.SearchScopeProvider, p.GetType().Name));

    // ── Unfiltered collections (settings UI ?show disabled as unchecked) ─

    public IEnumerable<IFilePreviewProvider> AllFilePreviewProviders => _previewProviders;
    public IEnumerable<IThumbnailProvider> AllThumbnailProviders => _thumbnailProviders;

    public IEnumerable<PluginActionRegistration> AllActions => _actions;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IDynamicActionProvider> AllDynamicActionProviders => _dynamicActionProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IInstantResultProvider> AllInstantResultProviders => _instantResultProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IFullSearchFileResultProvider> AllFullSearchFileResultProviders => _fullSearchFileResultProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchableItemProvider> AllSearchableItemProviders => _searchableItemProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISidebarFilterProvider> AllSidebarFilterProviders => _sidebarFilterProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IResultColumnProvider> AllResultColumnProviders => _resultColumnProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ITranslationProvider> AllTranslationProviders => _translationProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IThemeProvider> AllThemeProviders => _themeProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IQueryTokenProvider> AllQueryTokenProviders => _queryTokenProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider> AllQuickPanelTabProviders => _quickPanelTabProviders;
    public IEnumerable<PluginSdk.Abstractions.Plugins.ISearchScopeProvider> AllSearchScopeProviders => _searchScopeProviders;

    // ── Search and execution ──────────────────────────────────────────────

    public IEnumerable<PluginSearchActionMatch> SearchActionItems(string query, PluginSdk.Abstractions.SearchWindowType windowType, string? contextDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        if (windowType == PluginSdk.Abstractions.SearchWindowType.Inline && InlineSearchManager.Instance.ExplorerTracker.IsActiveWindowDialog) yield break;

        var tempResult = new SimpleSearchResult
        {
            ContextDirectory = contextDirectory ?? string.Empty,
            FullPath = string.Empty,
            IsDir = false
        };
        var single = new SimpleSearchResult[] { tempResult };

        foreach (var action in _actions)
        {
            if (action.Action.Keywords.Count == 0) continue;
            if (!PluginPerformanceMonitor.Measure(action.Action, () => action.Action.IsVisibleInSearch(single, windowType))) continue;
            if (!_filter.IsEnabled(ComponentFilter.GetDllName(action.Plugin), PluginComponentType.Action, action.Action.GetType().Name)) continue;
            if (!PluginPerformanceMonitor.Measure(action.Action, () => action.Action.CanExecute(single))) continue;

            var match = KeywordMatcher.TryMatchKeyword(query, action.Action.Keywords);
            if (match == null) continue;

            yield return new PluginSearchActionMatch(action, match.Value.Keyword, match.Value.ArgumentText);
        }
    }

    public bool TryExecuteSearchAction(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view, bool asAdmin = false)
        => PluginActionExecutor.TryExecute(result, view, asAdmin);

    public PluginActionRegistration? GetActionByRuntimeId(uint runtimeActionId)
        => _actions.FirstOrDefault(x => x.RuntimeActionId == runtimeActionId);
}

internal class SimpleSearchResult : PluginSdk.Abstractions.ISearchResult
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string ContextDirectory { get; set; } = string.Empty;
    public bool IsDir { get; set; }
    public bool IsApplication { get; set; }
    // Default (unknown) unless the producer had it already -- index-backed results (directory
    // enumeration, plugin directory search) carry the real values straight from the index.
    public PluginSdk.Abstractions.FileMetadata Metadata { get; set; }
}
