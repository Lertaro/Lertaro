using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Split out purely to keep PluginConfigFieldViewModel under the repo's per-file line limit; this class
// always operates on the one field that owns it. It owns that field's working-copy state: the child/array
// rows AND the "has the user staged an edit" flag, since both describe the same thing -- what this field
// currently holds in memory that is not yet written to settings.
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
    private bool _isDirty;

    internal PluginConfigFieldLoadSupport(PluginConfigFieldViewModel field)
    {
        _field = field;
        // Subscribed to the raw collection (not the ArrayItems getter, which would build the rows right
        // here and undo the lazy load) so every structural array change marks the field dirty.
        _arrayItems.CollectionChanged += OnArrayItemsChanged;
    }

    /// <summary>
    /// Whether this field (or, for a container, any of its rows) holds an edit the user made since the
    /// last commit. Checked WITHOUT materializing a tree that was never shown -- an unloaded container
    /// cannot have been edited, and forcing its rows to build here would make merely applying the
    /// Settings window walk every visited plugin's whole schema.
    /// </summary>
    internal bool IsDirty => _isDirty
        || (_childrenLoaded && _children.Any(c => c.IsDirty))
        || (_arrayItemsLoaded && _arrayItems.Any(i => i.IsDirty));

    /// <summary>Marks the field as holding a staged edit (a value change or a structural array change).</summary>
    internal void MarkDirty() => _isDirty = true;

    /// <summary>Clears the staged flag, after the value has been written to settings by Commit.</summary>
    internal void ClearDirty() => _isDirty = false;

    /// <summary>
    /// Whether the child rows are currently materialized. Read without triggering a load, so a test (or a
    /// diagnostic) can tell "dropped, waiting for a rebuild" from "never built" -- the public Children
    /// getter builds on demand and so cannot distinguish them.
    /// </summary>
    internal bool HasLoadedChildren => _childrenLoaded;

    /// <summary>
    /// Whether the array rows are currently materialized. Read without triggering a load, so
    /// <see cref="IsDirty"/> can ask "was this array ever shown (and so could it hold staged edits)?"
    /// without building rows nobody has looked at.
    /// </summary>
    internal bool HasLoadedArrayItems => _arrayItemsLoaded;

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

    private void OnArrayItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Add/Delete/Move/Duplicate all mutate this collection, and so does a drag-reorder straight
        // through the bound collection, which calls back through nothing else. A Reset is the load (and
        // Discard) repopulating/emptying it, not a user edit, so it is ignored.
        if (e.Action != NotifyCollectionChangedAction.Reset)
            _isDirty = true;
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

        // Populating the rows fired OnArrayItemsChanged; rebuilding from settings is not a user edit, so
        // the freshly loaded tree starts clean.
        _isDirty = false;
    }

    /// <summary>
    /// Drops everything loaded so the next access re-reads it from settings -- the staged-edit half of
    /// discarding edits, plus the rows those edits lived in. It deliberately does NOT repopulate: the
    /// collections are stable instances, so rebuilding here is only visible if something re-reads them,
    /// and the whole point is to keep that cost off the path that does not display the fields.
    /// </summary>
    internal void Reset()
    {
        _children.Clear();
        _arrayItems.Clear();
        _childrenLoaded = false;
        _arrayItemsLoaded = false;
        _isDirty = false;
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
