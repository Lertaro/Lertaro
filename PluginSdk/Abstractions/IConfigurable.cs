namespace Lertaro.PluginSdk.Abstractions;

public enum ConfigFieldType
{
    Boolean,
    Text,
    Integer,
    Choice,
    Array,
    Object,
    Group,
    StringList,
    Hotkey,
    FilePath,
    FolderPath,
    CustomControl,
    Button
}

public class PluginConfigField
{
    public string Key { get; set; } = string.Empty;
    public string GroupKey { get; set; } = string.Empty;
    public string LabelKey { get; set; } = string.Empty;
    public string DescriptionKey { get; set; } = string.Empty;
    public ConfigFieldType FieldType { get; set; }
    public object DefaultValue { get; set; } = null!;
    public List<string>? Choices { get; set; }
    public List<PluginConfigField>? SubFields { get; set; }
    /// <summary>For Hotkey fields: when true, single keys without modifier keys (Ctrl/Alt/Shift/Win) are rejected.</summary>
    public bool RequireModifier { get; set; }
    /// <summary>When true, saving this field with an empty/whitespace value falls back to <see cref="DefaultValue"/>
    /// instead of persisting the empty value -- for a field like a trigger keyword, where an empty value would
    /// silently make the depending feature unreachable rather than just "no value set".</summary>
    public bool RequireNonEmpty { get; set; }
    /// <summary>For Text fields: maximum character length (0 or unset means no length restriction).</summary>
    public int MaxLength { get; set; }
    /// <summary>For CustomControl fields: custom UI element/control hosted directly by the application.</summary>
    public object? CustomControl { get; set; }
    /// <summary>For Button fields: invoked when the button is clicked. A Button field stores no value;
    /// the click runs this delegate directly (e.g. a rebuild or clear action).</summary>
    public Action? OnClick { get; set; }
    /// <summary>Custom getter delegate for external plugin settings.</summary>
    public Func<object?>? GetValue { get; set; }
    /// <summary>Custom setter delegate for external plugin settings.</summary>
    public Action<object?>? SetValue { get; set; }
}

public class PluginConfigSchema
{
    public List<PluginConfigField> Fields { get; set; } = new();
    public Action? OnSave { get; set; }
    public Action? OnRollback { get; set; }
}

public interface IConfigurable
{
    PluginConfigSchema GetConfigSchema();
}
