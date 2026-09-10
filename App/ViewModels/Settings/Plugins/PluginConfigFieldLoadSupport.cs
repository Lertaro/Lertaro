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
    /// Drops everything loaded so the next access re-reads it from settings, which is what makes
    /// <see cref="PluginConfigFieldViewModel.Reload"/> still mean "discard staged edits". Rebuilds only
    /// what had actually been built: a tree nobody opened has nothing to discard, and re-populating it
    /// here would put the per-plugin cost straight back into the selection path the laziness keeps clear.
    /// </summary>
    internal void Reset()
    {
        _children.Clear();
        _arrayItems.Clear();
        var hadChildren = _childrenLoaded;
        var hadArrayItems = _arrayItemsLoaded;
        _childrenLoaded = false;
        _arrayItemsLoaded = false;

        if (hadChildren) EnsureChildrenLoaded();
        if (hadArrayItems) EnsureArrayItemsLoaded();
    }
}
