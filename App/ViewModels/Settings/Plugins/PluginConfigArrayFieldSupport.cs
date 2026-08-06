using Lertaro.App.Helpers;

namespace Lertaro.App.ViewModels.Settings.Plugins;

// Owns the Object/Array-field behavior for a PluginConfigFieldViewModel -- loading child items from
// settings, adding/removing array rows, and re-serializing children back into LocalValueStore. Split
// out purely to keep PluginConfigFieldViewModel under the file-length limit; this class has no
// state of its own, it always operates on the one field that owns it.
internal sealed class PluginConfigArrayFieldSupport
{
    private readonly PluginConfigFieldViewModel _field;

    public PluginConfigArrayFieldSupport(PluginConfigFieldViewModel field) => _field = field;

    public void LoadObjectChildren()
    {
        var rawSetting = _field.Settings.GetPluginSetting<object?>(_field.PluginId, _field.SchemaField.Key, null);
        var dict = ConfigValueHelper.UnpackValue(rawSetting) as Dictionary<string, object>
                   ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var sf in _field.SchemaField.SubFields!)
        {
            dict.TryGetValue(sf.Key, out var val);
            var childVM = new PluginConfigFieldViewModel(_field.PluginId, sf, _field.Settings, SaveObjectFromChildren)
            {
                LocalValueStore = ConfigValueHelper.UnpackValue(val ?? sf.DefaultValue)
            };
            _field.Children.Add(childVM);
        }
    }

    public void LoadArrayItems()
    {
        var rawSetting = _field.Settings.GetPluginSetting<object?>(_field.PluginId, _field.SchemaField.Key, null);
        var list = ConfigValueHelper.UnpackValue(rawSetting) as System.Collections.IEnumerable
                   ?? (_field.SchemaField.DefaultValue as System.Collections.IEnumerable);

        if (list != null)
        {
            var hasAnyVal = false;
            foreach (var item in list)
            {
                var unpackedItem = ConfigValueHelper.UnpackValue(item);
                if (unpackedItem is Dictionary<string, object> d)
                {
                    if (d.Values.Any(v => v != null && !string.IsNullOrWhiteSpace(v.ToString())))
                    {
                        hasAnyVal = true;
                        break;
                    }
                }
                else if (unpackedItem != null && !string.IsNullOrWhiteSpace(unpackedItem.ToString()))
                {
                    hasAnyVal = true;
                    break;
                }
            }

            if (!hasAnyVal && rawSetting != null)
                list = _field.SchemaField.DefaultValue as System.Collections.IEnumerable;

            if (list != null)
            {
                foreach (var item in list)
                    AddArrayItemViewModel(ConfigValueHelper.UnpackValue(item));
            }
        }

        _field.SelectedArrayItem = _field.ArrayItems.FirstOrDefault();
    }

    public void AddArrayItem()
    {
        object? newItem;
        if (_field.SchemaField.SubFields != null && _field.SchemaField.SubFields.Count > 0)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var sf in _field.SchemaField.SubFields)
                dict[sf.Key] = sf.DefaultValue;
            newItem = dict;
        }
        else
        {
            newItem = string.Empty;
        }

        AddArrayItemViewModel(newItem);
        _field.SelectedArrayItem = _field.ArrayItems[^1];
        SaveArrayFromChildren();
    }

    /// <summary>
    /// Copies the selected item and selects the copy. For a list whose entries differ in one field --
    /// a second custom command with the same action but another keyword -- which is otherwise filling
    /// every field in again by hand.
    /// </summary>
    public void DuplicateArrayItem()
    {
        if (_field.SelectedArrayItem is not { } source)
            return;

        // A fresh dictionary, not the one the source item holds: the copy has to be editable without
        // writing through to the item it was copied from.
        var value = source.GetValue();
        if (value is IDictionary<string, object> fields)
            value = new Dictionary<string, object>(fields, StringComparer.OrdinalIgnoreCase);

        AddArrayItemViewModel(value);
        _field.SelectedArrayItem = _field.ArrayItems[^1];
        SaveArrayFromChildren();
    }

    private void AddArrayItemViewModel(object? itemValue)
    {
        PluginConfigArrayItemViewModel? itemVM = null;
        itemVM = new PluginConfigArrayItemViewModel(_field, itemValue,
            onDelete: () =>
            {
                var wasSelected = ReferenceEquals(_field.SelectedArrayItem, itemVM);
                var index = _field.ArrayItems.IndexOf(itemVM!);
                _field.ArrayItems.Remove(itemVM!);
                if (wasSelected)
                    _field.SelectedArrayItem = _field.ArrayItems.Count > 0 ? _field.ArrayItems[Math.Min(index, _field.ArrayItems.Count - 1)] : null;
                SaveArrayFromChildren();
            },
            onMoveUp: () => MoveArrayItem(itemVM!, -1),
            onMoveDown: () => MoveArrayItem(itemVM!, 1));
        _field.ArrayItems.Add(itemVM);
    }

    // Backs both the master list's Move Up/Down buttons and (indirectly, via the same underlying
    // ObservableCollection.Move a drag-drop reorder also calls) the drag-to-reorder handle -- see
    // DragReorder in FieldRowTemplate.xaml's ListBox. SaveArrayFromChildren keeps LocalValueStore in
    // sync immediately, same as every other array mutation here (Add/Delete), even though Commit()
    // would re-derive it fresh from ArrayItems' current order regardless once the window is confirmed.
    internal void MoveArrayItem(PluginConfigArrayItemViewModel item, int direction)
    {
        var index = _field.ArrayItems.IndexOf(item);
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _field.ArrayItems.Count) return;

        _field.ArrayItems.Move(index, newIndex);
        SaveArrayFromChildren();
    }

    public void SaveObjectFromChildren()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in _field.Children)
            dict[child.SchemaField.Key] = child.LocalValueStore;
        _field.CommitLocalValue(dict);
    }

    public void SaveArrayFromChildren() => _field.CommitLocalValue(_field.ArrayItems.Select(item => item.GetValue()).ToList());
}
