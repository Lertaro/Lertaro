namespace Lertaro.App.ViewModels.Settings.Plugins;

// Split out purely to keep PluginConfigFieldViewModel under the repo's per-file line limit; this class
// has no state of its own, it always operates on the one field that owns it. Owns what "commit this
// field" means -- flattening an Object/Array field's rows back into its stored value, then writing the
// value through to settings (or the schema's own SetValue hook).
internal static class PluginConfigFieldCommitSupport
{
    internal static void Commit(PluginConfigFieldViewModel field)
    {
        // Nothing to store for a non-value row: a custom control is hosted UI, and a button runs
        // its OnClick delegate directly -- persisting either would write meaningless settings keys.
        // Their staged flags are cleared anyway so a no-op row cannot keep a plugin looking dirty.
        if (field.IsCustomControl || field.IsButton)
        {
            field.ClearDirty();
            return;
        }
        if (field.IsGroup)
        {
            foreach (var child in field.Children)
            {
                child.Commit();
            }
            field.ClearDirty();
            return;
        }

        if (field.IsObject)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in field.Children)
            {
                child.Commit();
                dict[child.SchemaField.Key] = child.LocalValueStore;
            }
            field.SetRawLocalValue(dict);
        }
        else if (field.IsArray)
        {
            var list = new List<object?>();
            foreach (var item in field.ArrayItems)
            {
                list.Add(item.GetValue());
                item.ClearDirty();
            }
            field.SetRawLocalValue(list);
        }

        if (!field.HasValueChangedCallback)
        {
            if (field.SchemaField.SetValue != null)
            {
                field.SchemaField.SetValue(field.LocalValueStore);
            }
            else if (field.IsStringList && field.LocalValueStore is System.Collections.IEnumerable en && !(field.LocalValueStore is string))
            {
                var cleaned = new List<string>();
                foreach (var item in en) { var s = item?.ToString()?.Trim(); if (!string.IsNullOrEmpty(s)) cleaned.Add(s); }
                field.Settings.SetPluginSetting(field.PluginId, field.SchemaField.Key, cleaned);
            }
            else
            {
                // A RequireNonEmpty field (e.g. a trigger keyword) left blank in the UI would otherwise
                // persist as "", silently making whatever depends on it unreachable rather than falling
                // back to a sane default -- force the schema's own DefaultValue back in at save time.
                var toSave = field.LocalValueStore;
                if (field.SchemaField.RequireNonEmpty && (toSave == null || (toSave is string s && string.IsNullOrWhiteSpace(s))))
                {
                    toSave = field.SchemaField.DefaultValue;
                    field.SetRawLocalValue(toSave);
                    field.NotifyValueChanged();
                }
                field.Settings.SetPluginSetting(field.PluginId, field.SchemaField.Key, toSave);
            }
        }

        field.ClearDirty();
    }
}
