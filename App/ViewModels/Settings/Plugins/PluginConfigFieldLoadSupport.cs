using System.Collections.ObjectModel;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Split out purely to keep PluginConfigFieldViewModel under the repo's per-file line limit; this class
// has no state of its own beyond the two collections and their loaded flags, and always operates on the
// one field that owns it.
//
// It owns WHEN a field's child rows and array rows get built: on first access, not in the constructor.
// Only the one plugin whose config tab is open ever renders them, yet the plugin list used to build every
// plugin's whole tree up front -- reading settings and deserializing array rows per field, recursing
// through groups -- which was the bulk of the page's first-open cost. The collections are handed out as
// the same instances throughout, so XAML bindings stay valid across the load.
internal sealed class PluginConfigFieldLoadSupport
{
    private readonly PluginConfigFieldViewModel _field;
    private readonly ObservableCollection<PluginConfigFieldViewModel> _children = new();
    private readonly ObservableCollection<PluginConfigArrayItemViewModel> _arrayItems = new();
    private bool _childrenLoaded;
    private bool _arrayItemsLoaded;
    private bool _loadInProgress;

    internal PluginConfigFieldLoadSupport(PluginConfigFieldViewModel field) => _field = field;

    /// <summary>
    /// Whether the child rows are currently materialized. Read without triggering a load, so a test (or a
    /// diagnostic) can tell "dropped, waiting for a rebuild" from "never built" -- the public Children
    /// getter builds on demand and so cannot distinguish them.
    /// </summary>
    internal bool HasLoadedChildren => _childrenLoaded;

    internal ObservableCollection<PluginConfigFieldViewModel> Children
    {
        get
        {
            EnsureChildrenLoaded();
            return _children;
        }
    }

    internal ObservableCollection<PluginConfigArrayItemViewModel> ArrayItems
    {
        get
        {
            EnsureArrayItemsLoaded();
            return _arrayItems;
        }
    }

    private void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || _loadInProgress) return;
        // A field that owns a change callback (an object child or an array item's sub-field) never built
        // its own children -- the constructor this replaces gated on exactly this.
        if (_field.HasValueChangedCallback) return;
        _childrenLoaded = true;

        if (_field.IsGroup && _field.SchemaField.SubFields != null)
        {
            foreach (var sf in _field.SchemaField.SubFields)
            {
                // Group children carry no change callback: a group is a layout section, and each leaf
                // writes itself to settings when Commit runs.
                _children.Add(new PluginConfigFieldViewModel(_field.PluginId, sf, _field.Settings, null));
            }
        }
        else if (_field.IsObject && _field.SchemaField.SubFields != null)
        {
            // LoadObjectChildren adds through the field's own Children property, so guard against the
            // getter re-entering this method while that runs.
            _loadInProgress = true;
            try { _field.ArraySupport.LoadObjectChildren(); }
            finally { _loadInProgress = false; }
        }
    }

    private void EnsureArrayItemsLoaded()
    {
        if (_arrayItemsLoaded || _loadInProgress) return;
        // Same condition the constructor used: only a top-level (callback-less) array field loads items.
        if (_field.HasValueChangedCallback || !_field.IsArray) return;
        _arrayItemsLoaded = true;

        _loadInProgress = true;
        try { _field.ArraySupport.LoadArrayItems(); }
        finally { _loadInProgress = false; }
    }

    /// <summary>
    /// Drops everything loaded so the next access re-reads it from settings -- the cheap half of
    /// discarding staged edits. It deliberately does NOT repopulate: the collections are stable
    /// instances, so rebuilding here is only visible if something re-reads them, and the whole point is
    /// to keep that cost off the path that does not display the fields.
    /// </summary>
    internal void Reset()
    {
        _children.Clear();
        _arrayItems.Clear();
        _childrenLoaded = false;
        _arrayItemsLoaded = false;
    }

    /// <summary>
    /// Discards staged state AND rebuilds whatever had been loaded, for when the fields are about to be
    /// shown again. <see cref="Reset"/> alone leaves the (stable) collections empty, and a control already
    /// bound to them does not re-read the property, so the rebuild has to happen before showing.
    /// </summary>
    internal void Reload()
    {
        var hadChildren = _childrenLoaded;
        var hadArrayItems = _arrayItemsLoaded;

        Reset();

        if (hadChildren) EnsureChildrenLoaded();
        if (hadArrayItems) EnsureArrayItemsLoaded();
    }
}
